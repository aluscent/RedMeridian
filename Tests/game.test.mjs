import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { fileURLToPath } from "node:url";
import { runInNewContext } from "node:vm";

const source = new URL("../Source/", import.meta.url);
const play = new URL("../Play/", import.meta.url);
const readJson = async url => JSON.parse(await readFile(url, "utf8"));

async function pngDimensions(edition, image) {
  const png = await readFile(new URL("wwwroot/" + image, edition));
  assert.equal(png.subarray(0, 8).toString("hex"), "89504e470d0a1a0a", image);
  assert.equal(png.toString("ascii", 12, 16), "IHDR", image);
  return [png.readUInt32BE(16), png.readUInt32BE(20)];
}

test("both editions include the configured PNGs and correctly sized terrain assets", async () => {
  for (const edition of [source, play]) {
    const buildings = await readJson(new URL("Config/buildings.json", edition));
    const images = [...new Set(buildings.flatMap(building =>
      [building.image, ...Object.values(building.environmentImages ?? {})]).filter(Boolean))];
    assert.ok(images.length > 0, "expected a catalog with custom building art");
    for (const image of images) {
      const [width, height] = await pngDimensions(edition, image);
      assert.ok(width > 0 && height > 0, image);
    }
    assert.deepEqual(await pngDimensions(edition, "assets/martian-ground.png"), [784, 50]);
    for (const id of ["iron", "ice", "silicon", "titanium", "brine", "uranium", "vent"]) {
      assert.deepEqual(await pngDimensions(edition, `assets/deposit-${id}.png`), [98, 50], id);
    }
  }
});

test("live game serves the map, rejects stale edits, and reloads an isolated save", { timeout: 30_000 }, async t => {
  const saveDir = await mkdtemp(join(tmpdir(), "red-meridian-tests-"));
  const server = spawn("dotnet", ["bin/Release/net8.0/RedMeridian.dll", "--urls", "http://127.0.0.1:0"], {
    cwd: fileURLToPath(source),
    env: { ...process.env, RED_MERIDIAN_SAVE: join(saveDir, "colony.json") },
    stdio: ["ignore", "pipe", "pipe"]
  });
  const closed = new Promise(resolve => server.once("close", resolve));
  t.after(async () => {
    server.kill();
    await closed;
    await rm(saveDir, { recursive: true, force: true });
  });
  const base = await new Promise((resolve, reject) => {
    let log = "";
    server.once("error", reject);
    server.once("close", code => reject(new Error(`Server exited with ${code}\n${log}`)));
    let ready = false;
    server.stderr.on("data", chunk => { if (!ready) log += chunk; });
    server.stdout.on("data", chunk => {
      if (ready) return;
      log += chunk;
      const address = log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
      if (address) {
        ready = true;
        resolve(address[1]);
      }
    });
    t.signal.addEventListener("abort", () => reject(t.signal.reason), { once: true });
  });

  async function get(path) {
    const response = await fetch(base + path, { signal: t.signal });
    assert.equal(response.status, 200, path);
    return response;
  }

  async function command(action, fields = {}, expectedStatus = 200) {
    const response = await fetch(base + "/api/command", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action, ...fields }),
      signal: t.signal
    });
    const result = await response.json();
    assert.equal(response.status, expectedStatus, `${action}: ${result.message}`);
    return result;
  }

  const catalog = await (await get("/api/catalog")).json();
  const network = await (await get("/api/logistics")).json();
  assert.deepEqual(network.bases.map(base => base.id), ["mars"]);
  assert.ok(network.sites.some(site => site.kind === "orbit" && site.width === 5 && site.height === 150));
  assert.ok(network.sites.some(site => site.kind === "moon" && site.width === 316 && site.height === 316));
  assert.match(await (await get("/")).text(), /game\.js/);
  const mine = catalog.buildings.find(building => building.deposit === "iron");
  const image = await get("/" + mine.image);
  assert.match(image.headers.get("content-type"), /^image\/png\b/);
  await image.arrayBuffer();

  const initial = await (await get("/api/state")).json();
  assert.equal(initial.started, false);
  const site = catalog.landingSite;
  const first = site.top * 1024 + site.left;
  const map = { seed: -123456, abundance: 175, paint: { [first]: "iron", [first + 1]: null } };
  await command("new", { map });
  const { state } = await command("pause");
  assert.deepEqual(await (await get("/api/surface")).json(), map);
  await command("build", { index: first, type: mine.id, revision: state.revision - 1 }, 400);

  // Find a natural ore deposit with the shipped browser code, then check the server agrees.
  const editor = await (await get("/editor.js")).text();
  const makeGeology = runInNewContext(editor + "\nmakeGeology");
  const geology = makeGeology(catalog, catalog.world.deposits);
  const naturalOre = Array.from({ length: site.width - 2 }, (_, i) => first + i + 2)
    .find(index => geology.at(index, map)?.id === mine.deposit);
  assert.ok(Number.isInteger(naturalOre), "fixture seed should expose natural iron");
  await command("build", { index: first + 1, type: mine.id, revision: state.revision }, 400);
  const placed = await command("build", { index: naturalOre, type: mine.id, revision: state.revision });
  assert.deepEqual(placed.edits, [{ index: naturalOre, type: mine.id, width: 1, height: 1 }]);

  const response = await get("/api/map");
  const tiles = new Uint8Array(await response.arrayBuffer());
  const area = site.width * site.height;
  const offset = naturalOre - first;
  assert.equal(tiles.length, area * 3);
  assert.equal(Number(response.headers.get("X-Map-Revision")), placed.state.revision);
  assert.equal(tiles[offset], mine.id);
  assert.equal(tiles[offset + area], 1);
  assert.equal(tiles[offset + 2 * area], 1);
  assert.equal(tiles[0], 0, "stale request did not mutate the map");
  await command("save");
  await command("remove", { index: naturalOre, revision: placed.state.revision });
  await command("load");
  const restored = await (await get(`/api/state?inspect=${naturalOre}`)).json();
  assert.equal(restored.inspected.type, mine.id);
  assert.equal(restored.hasSave, true);
  assert.equal(restored.site.width, site.width);
  assert.equal(restored.site.height, site.height);

  const save = await readJson(join(saveDir, "colony.json"));
  const orbit = network.sites.find(site => site.kind === "orbit");
  const last = (orbit.top + orbit.height - 1) * 1024 + orbit.left + orbit.width - 1;
  const solar = catalog.buildings.find(building => building.art === "solar");
  const remote = { ...save, tiles: [[last, solar.id]], structures: [], longOrbit: true,
    planetBounds: null, weatherForecasts: null, map: { seed: 1, abundance: 0, paint: {} }, space: null };
  save.space = { bases: { [orbit.id]: remote }, ships: [] };
  const imported = await command("import", { save, revision: restored.revision });
  const selected = await command("select-base", { base: orbit.id, revision: imported.state.revision });
  const remoteResponse = await get("/api/map");
  const orbitCells = new Uint8Array(await remoteResponse.arrayBuffer());
  const orbitArea = orbit.width * orbit.height;
  assert.equal(Number(remoteResponse.headers.get("X-World-Revision")), selected.state.worldRevision);
  assert.equal(orbitCells.length, 3 * orbitArea);
  assert.equal(orbitCells[orbitArea - 1], solar.id);
  assert.equal(orbitCells[2 * orbitArea - 1], 1);
  assert.equal(orbitCells[3 * orbitArea - 1], 1);
  await command("build", { index: last + 1, type: solar.id, revision: selected.state.revision }, 400);
  const removed = await command("remove", { index: last, revision: selected.state.revision });
  assert.equal(removed.edits[0].index, last);
});
