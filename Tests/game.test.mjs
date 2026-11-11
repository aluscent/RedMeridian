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

test("both editions include the building sheets, separate resource icons, and terrain assets", async () => {
  for (const edition of [source, play]) {
    const buildings = await readJson(new URL("Config/buildings.json", edition));
    const images = [...new Set(buildings.flatMap(building =>
      [building.image, ...Object.values(building.environmentImages ?? {})]).filter(Boolean))];
    for (const image of images) {
      const [width, height] = await pngDimensions(edition, image);
      assert.ok(width > 0 && height > 0, image);
    }
    for (const [file, dimensions] of [
      ["planets-structures", [2048, 1230]],
      ["planet-buildings", [672, 336]],
      ["orbit-structures", [1100, 880]]
    ]) {
      assert.deepEqual(await pngDimensions(edition, `assets/${file}.png`), dimensions);
    }
    for (const id of ["water", "ore", "food", "alloy", "research", "silicon", "titanium", "brine", "uranium", "propellant",
      "power-margin", "battery", "colonists"]) {
      assert.deepEqual(await pngDimensions(edition, `assets/resource-${id}.png`), [84, 84], id);
    }
    assert.deepEqual(await pngDimensions(edition, "assets/martian-ground.png"), [784, 50]);
    for (const id of ["iron", "ice", "silicon", "titanium", "brine", "uranium", "vent"]) {
      assert.deepEqual(await pngDimensions(edition, `assets/deposit-${id}.png`), [98, 50], id);
    }
  }
});

test("shared sprite placement anchors rectangular footprints and sorts back to front", async () => {
  const { default: iso } = await import("../Source/wwwroot/isometric.js");
  const screen = (x,y) => iso.project(x,y,1.25,30,40);
  const rear = iso.place(screen,1.25,4,1,1,1,1);
  const wide = iso.place(screen,1.25,26,2,1,3,2);
  const [x,y,,,,scale] = wide;
  assert.equal(x,screen(3.5,2)[0]);
  assert.equal(y+50*scale,screen(5,3)[1]);
  assert.equal(scale,3.125);
  assert.deepEqual([wide,rear].sort(iso.depth),[rear,wide]);
  for(const kind of ["mars","orbit"]){
    const polygons=[];
    globalThis.Sprites={polygon:(_ctx,points)=>polygons.push(points)};
    try {
      iso.drawFoundation({},screen,kind,wide);
      assert.deepEqual(polygons[2],[screen(2,1),screen(5,1),screen(5,3),screen(2,3)]);
      iso.drawFoundation({},screen,kind,rear);
      assert.equal(polygons.length,4);
    } finally {
      delete globalThis.Sprites;
    }
  }
});

test("partial galaxy art falls back per asset in the same environment", async () => {
  const { default: artwork } = await import("../Source/wwwroot/artwork.js");
  const manifest = { orbit: ["building-1", "resource-water"] };
  const load = async url => url.endsWith("/building-1.png") ? "custom solar" : null;
  const fallback = async (env, kind, key) => env + ":" + kind + ":" + key;
  assert.equal(await artwork.resolve("test-galaxy", "orbit", "building", 1, manifest, load, fallback), "custom solar");
  assert.equal(await artwork.resolve("test-galaxy", "orbit", "resource", "water", manifest, load, fallback), "orbit:resource:water");
  assert.equal(await artwork.resolve("test-galaxy", "planet", "building", 1, manifest, load, fallback), "planet:building:1");
  assert.equal(await artwork.resolve("test-galaxy", "orbit", "building", 2, manifest, load, fallback), "orbit:building:2");
});

test("live game serves the map, rejects stale edits, and reloads an isolated save", { timeout: 30_000 }, async t => {
  const saveDir = await mkdtemp(join(tmpdir(), "red-meridian-tests-"));
  const server = spawn("dotnet", ["bin/Release/net8.0/RedMeridian.dll", "--urls", "http://127.0.0.1:0", "--developer", "true"], {
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
  for (const name of ["planet-buildings", "planets-structures", "orbit-structures", "resource-ore"]) {
    const image = await get(`/assets/${name}.png`);
    assert.match(image.headers.get("content-type"), /^image\/png\b/);
    await image.arrayBuffer();
  }

  const initial = await (await get("/api/state")).json();
  assert.equal(initial.started, false);
  const site = catalog.landingSite;
  const first = site.top * 1024 + site.left;
  for (const abundance of [2, 16]) await command("new", { map: { abundance } }, 400);
  for (const abundance of [3, 15]) await command("new", { map: { abundance } });
  const map = { seed: -123456, abundance: 15, densityPercent: true, paint: { [first]: "iron", [first + 1]: null } };
  await command("new", { map });
  await command("pause");
  await command("save", { saveSlot: "../outside" }, 400);
  await command("save", { saveSlot: "First landing" });
  await command("save", { saveSlot: "First landing" }, 400);
  await command("save", { saveSlot: "Second landing" });
  assert.deepEqual((await (await get("/api/saves")).json()).map(slot => slot.id).sort(),
    ["First landing", "Second landing"]);
  await command("menu");
  await command("load", { saveSlot: "First landing" });
  assert.deepEqual(await (await get("/api/surface")).json(), map);
  await command("new", { map });
  const { state } = await command("pause");
  await command("tester-grant", { revision: state.revision, material: "alloy", amount: 100 });
  assert.deepEqual(await (await get("/api/surface")).json(), map);
  await command("build", { index: first, type: mine.id, revision: state.revision - 1 }, 400);

  // Find a natural ore deposit with the shipped browser code, then check the server agrees.
  const editor = await (await get("/editor.js")).text();
  const makeGeology = runInNewContext(editor + "\nmakeGeology");
  const geology = makeGeology(catalog, catalog.world.deposits);
  const naturalOre = Array.from({ length: site.width * 4 - 2 }, (_, i) =>
    first + Math.floor((i+2)/site.width)*1024 + (i+2)%site.width)
    .find(index => geology.at(index, map)?.id === mine.deposit);
  assert.ok(Number.isInteger(naturalOre), "fixture seed should expose natural iron");
  await command("build", { index: first + 1, type: mine.id, revision: state.revision }, 400);
  const placed = await command("build", { index: naturalOre, type: mine.id, revision: state.revision });
  assert.deepEqual(placed.edits, [{ index: naturalOre, type: mine.id, width: 1, height: 1 }]);

  const response = await get("/api/map");
  const tiles = new Uint8Array(await response.arrayBuffer());
  const area = site.width * site.height;
  const offset = Math.floor((naturalOre-first)/1024)*site.width + (naturalOre-first)%1024;
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
  const orbitStart = orbit.top * 1024 + orbit.left;
  const last = (orbit.top + orbit.height - 1) * 1024 + orbit.left + orbit.width - 1;
  const solar = catalog.buildings.find(building => building.art === "solar");
  const drill = catalog.buildings.find(building => building.art === "drill");
  const remote = { ...save, tiles: [[last, solar.id], [orbitStart, drill.id]],
    structures: [{ index: orbitStart, width: 2, height: 2 }], longOrbit: true,
    planetBounds: null, weatherForecasts: null, map: { seed: 1, abundance: 0, paint: {} }, space: null };
  const terminator = network.sites.find(site => site.id === "terminator");
  const climateBase = { ...remote, longOrbit: false, tiles: [[511*1024+511, solar.id]],
    structures: [],
    planetBounds: { width: terminator.width, height: terminator.height },
    map: { seed: terminator.seed, abundance: 10, paint: {}, densityPercent: true } };
  save.space = { bases: { [orbit.id]: remote, [terminator.id]: climateBase }, ships: [] };
  const imported = await command("import", { save, revision: restored.revision });
  const selected = await command("select-base", { base: orbit.id, revision: imported.state.revision });
  const { default: artwork } = await import("../Source/wwwroot/artwork.js");
  assert.deepEqual([...selected.state.allowedBuildings].sort((a, b) => a - b),
    catalog.buildings.filter(building => artwork.hasOrbitStructure(building.id)).map(building => building.id));
  assert.ok(!selected.state.allowedBuildings.includes(drill.id));
  const remoteResponse = await get("/api/map");
  const orbitCells = new Uint8Array(await remoteResponse.arrayBuffer());
  const orbitArea = orbit.width * orbit.height;
  assert.equal(Number(remoteResponse.headers.get("X-World-Revision")), selected.state.worldRevision);
  assert.equal(orbitCells.length, 3 * orbitArea);
  assert.equal(orbitCells[0], drill.id, "existing orbital drills survive save imports");
  assert.equal(orbitCells[orbitArea], 2);
  assert.equal(orbitCells[2 * orbitArea], 2);
  assert.equal(orbitCells[orbitArea - 1], solar.id);
  assert.equal(orbitCells[2 * orbitArea - 1], 1);
  assert.equal(orbitCells[3 * orbitArea - 1], 1);
  await command("build", { index: last + 1, type: solar.id, revision: selected.state.revision }, 400);
  await command("build", { index: orbitStart + 2, type: drill.id, revision: selected.state.revision }, 400);
  const removed = await command("remove", { index: last, revision: selected.state.revision });
  assert.equal(removed.edits[0].index, last);
  const climateSite = await command("select-base", { base: terminator.id, revision: removed.state.revision });
  const terrain = await get("/api/climate");
  const before = new DataView(await terrain.arrayBuffer());
  assert.equal(before.byteLength, terminator.width * terminator.height * 5);
  assert.equal(Number(terrain.headers.get("X-World-Revision")), climateSite.state.worldRevision);
  const changed = await command("climate", { climate: { baseTemperature: 10, humidity: 0 }, revision: climateSite.state.revision });
  const after = new DataView(await (await get("/api/climate")).arrayBuffer());
  assert.equal(changed.state.climate.baseTemperature, 10);
  assert.equal(after.getInt16(2,true)-before.getInt16(2,true), 100);
});
