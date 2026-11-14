# Red Meridian — Map Edition

Every new campaign starts on Mars in the Solar System and generates its own
universe. Mars defaults to 10,000 tiles. Adjust its landing bounds under
`planetSizes` in `Config/world.json`. Existing colonies retain their saved
bounds, and tester mode still offers the 100,000-structure stress map.

Install the .NET 8 SDK, run **Start Red Meridian.cmd**, and open
http://127.0.0.1:5287/. Keep the server window open. Both Windows launchers
build and run the project in `Source`, where the assets and catalogs are maintained.

To make a standalone Windows build from the repository root, run:

```sh
dotnet publish Source/RedMeridian.csproj -c Release -r win-x64 --self-contained false -o dist/RedMeridian
```

On a machine with the ASP.NET Core 8 Runtime, run `RedMeridian.exe` from
the published folder and open http://127.0.0.1:5274/.

## Run the tests

Use the .NET 8 SDK and Node.js 22 or newer. From the project root, run:

```sh
dotnet run --project Tests/RedMeridian.Tests.csproj --configuration Release
node --test Tests/game.test.mjs
```

The first command runs the domain tests and builds the server. The second
checks sprite assets and exercises the live API, including browser/server map
agreement and a save/load round trip. It starts its own server on a temporary
local port and uses a separate save directory. GitHub Actions runs the suite
and checks JavaScript syntax on pushes and pull requests.

### Run the headless stress tester

The runner uses the game simulation without a browser or server. From the
project root, run this command for 100 surface regions and 30,000 spacecraft:

```sh
dotnet run --project Tools/Stress/RedMeridian.Stress.csproj -c Release -- --regions 100 --orbits 0 --ships 30000 --routes 30000 --batches 5 --speed 4
```

`--regions` includes the Mars landing region. Try 99, 100, 999, and 1,000
regions to compare the planned engine thresholds. Each run uses a synthetic
universe with mixed production, shortages, and a simultaneous wave of freight
arrivals. The runner measures 30-second-equivalent batches of the current
engine, then serializes and reloads the save in memory. Its timings exclude UI,
network, and disk I/O. Due cargo still in transit includes blocked deliveries.
Colony maps store building anchors and footprints. The server constructs a
tile layout on demand when the browser requests the selected map.

## Forecast dust storms

Build a **Weather Observatory** from the Research category and inspect it after
it has operated for a tick. The forecast narrows as the next storm approaches.
Battery cover uses recorded demand, stored charge, and the storm output of
generators that run without fuel or crew. An offline observatory keeps its last
reading, including its original observation tick.

## Expand into space

1. On Mars, build and power a **Space communications relay**, a **Spacecraft
   assembly yard**, and a **Surface cargo terminal**. Produce propellant.
2. Inspect the **Spacecraft Assembly Yard** and select a spacecraft type to start
   assembly. It advances when the yard and cargo facility are operating. When
   the ship is ready, click **Space travel** above the map, select the spacecraft,
   and load cargo up to its capacity. A founding mission also costs
   24 alloy and installs a Spacecraft Hub that can receive cargo without power.
   Pack food, water, alloy for power, housing and a relay, and return propellant.
3. After arrival, switch views from the same panel. Build a Research Lab at an
   orbital station and reach 30 RP there. Ceres offers iron, titanium, and
   uranium. Europa offers ice, brine, and silicon, with weaker solar output.
4. For an established destination, schedule a ship with a material, source
   reserve, and destination reorder threshold. It makes repeat trips as
   supplies and fuel allow. Leave return propellant at the destination.

All bases advance together. Cargo remains aboard if the destination has no
working cargo facility or storage is full. Saving captures bases, flights,
routes, discoveries, and surveys in progress.
Older single-colony saves open on Mars with an empty fleet.
New orbital stations are 5 tiles wide and 150 tiles long. Existing orbital
stations keep their saved layouts.

## Explore beyond the Solar System

The landing planner starts with a fresh campaign seed. Enter a previous seed
to reproduce that universe with the same generator version. The planner's
abundance and paint controls also determine the starting Mars deposits.
The generator varies neighboring galaxies, star systems, destinations,
distances, climate, and regional deposits. The ten distant galaxies keep
their specialty materials, with local availability of optional research and
its buildings varying by campaign. The game checks the landing recipe chain
and the discovery and travel ranges before opening a campaign.

The save keeps the seed and generator version. Detailed terrain and deposit
maps are evaluated when a colony is founded or loaded. Older saves keep the
original fixed chart, and undiscovered destinations stay off the star map.

Activate **Orbital optics**, then build and power a **Space Telescope** at an
orbital base. Inspect it and select **Explore**. A star-system survey costs
300 RP and takes eight operating sols. The telescope's range rises from
600 to 18,000 to 540,000 to 16,200,000 UoD across its four levels. From level 3, a galaxy
survey costs 2,000 RP and takes 30 operating sols. Surveys pause whenever the
telescope loses power or crew. Discovering a galaxy reveals its primary
system. To survey other systems there, establish an orbital telescope in
that galaxy.

The zoomable **Star Map** tab labels known planets, systems, and galaxies.
Select a destination in **Fleet & launch** to see the
ship-specific fuel and time estimate. Stellar Explorers reach other systems
in the Milky Way, Galaxy Clippers reach neighboring galaxies, and Deep Galaxy
Arks can reach the ten distant galaxies. Those galaxies have their own worlds,
survey milestones, and specialty materials. Each specialty material appears
in inventories and cargo controls after the first spacecraft arrives in its galaxy.

## Activate research milestones

Research Labs generate research points. Open **Research milestones** and click
**Activate** when the colony has enough points and its prerequisites are active.
Activation uses the selected base's points as a threshold and applies to all
bases within its galaxy. Older saves retain the combined milestones unlocked
across their Solar System bases.

Edit `Source/Config/spacecraft.json` to configure spacecraft types. Each entry
has an `id`, `name`, `payload`, `fuelPercent`, `buildTicks`, and a `cost`
object keyed by material ID. `range` is the maximum route distance in UoD,
`speed` is UoD per sol, and `fuelDistance` scales cruise fuel with distance.
`research` names the milestone needed to assemble the ship.
Keep IDs stable because spacecraft and assembly orders reference them in saves.

## Tester mode

Run **Start Red Meridian Tester.cmd** and open http://127.0.0.1:5288/.
Open **Construction → Tester tools** to grant one amount to each checked
resource or unlock every available research milestone in the current galaxy.
The section also offers manual tick advancement, the 100,000-structure
stress map, and its pan test. Tester mode defaults to
`colony.test.json` in the save directory, while normal play uses `colony.json`.

## Generate, edit, or load a map

The launch screen previews the geology before the colony clock starts.
Enter a signed 32-bit seed or roll one. The same seed, abundance, and paint
produce the same deposit layout. Abundance targets a 3%–15% share of tiles
with natural deposits on new maps. Deposits are continuous sources.
The starter iron and ice deposits are protected at every abundance setting.

Open **Map editor** to paint any configured deposit, clear it, or restore the
generated tile. Pan with the arrow buttons. The light outlines mark the
starting outpost. **Export map** and **Import map** share terrain designs as
JSON. A map design contains a seed, abundance, and paint overrides.

## Developer sprite preview

Run `node Tools/sprite-preview.mjs` and open `http://127.0.0.1:5290`.
The page loads the source catalog, artwork, terrain, and renderer without
starting the game or opening a save. Compare up to three adjacent buildings
on planetary or orbital terrain. Change the direction, spacing, and footprint
outlines to check how the sprites align and overlap. Enable **Full catalog**
to inspect every building available in the selected environment. The game
and preview draw the same grid-aligned foundations for multi-tile buildings.

## The Terminator

Found the Terminator from **Space travel**. Open **Map climate** to view
sunlight, temperature, and elevation overlays, or to change the base
temperature and humidity. The temperature field uses fixed tile offsets.
Humidity narrows those offsets and adds greenhouse warming above a configured
threshold.

Solar output follows local sunlight. Temperature limits apply to the entire
footprint. Frozen oceans and dry seabeds can be built on. Melting or refilling
can destroy buildings, including buildings on shore. After a flood, liquid
shore can be built on again. New Terminator colonies use a 30 × 360 meridian
with five-tile cold and hot bands. Adjacent tiles differ by at most two
elevation levels, and a new building footprint can span three levels
(for example, elevations 7 through 9). Terrain shading shows relief,
sunlight, temperature, and water depth on the normal map.

Mars gains one colonist every six in-game hours below 100 population,
then gains 1% every six hours. Both rates require spare housing and supply
reserves. Fractional percentage growth carries over to later arrivals. A powered and
staffed Human production facility adds one colonist per in-game hour on any
base with spare housing and supplies for the additional colonist.

Climate profiles and engineering duration live
in `Config/world.json`, and building temperature limits live in
`Config/buildings.json`. Planetary engineering requires at least 200 sols
of effective work per metric from its starting value.

**Load saved colony** opens the save selected in the main menu. The original
`%LOCALAPPDATA%\RedMeridian\colony.json` remains available as **Default colony**.
Use **Save as** to create a named slot. **Save colony** updates the active slot.
Named slots live next to the default file in `colony.json.slots`.
New saves use gzip compression and keep the existing slot filenames. The game
can still load and import older plain JSON saves. **Import colony file** accepts
either format up to 64 MiB on disk and 256 MiB after decompression.

Open **Control Panel** for research milestones, engineering progress, shipyard
orders, the fleet and launch controls, and the star map. The star map lists
known destinations and their distances from the selected star system. **Construction**
opens a side panel within the map.

Default artwork is in `wwwroot/assets`. `planets-structures.png` supplies
dedicated art for major surface structures, and `planet-buildings.png`
supplies the rest of the surface buildings. The renderer centers square
foundations within rectangular footprints. `orbit-structures.png` supplies
18 orbital facility sprites. The Space Telescope and Spacecraft Hub use
built-in sprites. Existing orbital core drills still
load from saves and use the procedural drill sprite. Separate
`resource-<id>.png` icons are shared by the surface and orbit interfaces.
To add galaxy-specific art, place an
`artwork.json` in `wwwroot/assets/galaxies/<galaxy-id>/`. Its optional
`planet` and `orbit` arrays list asset names like `building-1` or
`resource-water`. Put the PNG in the corresponding environment directory.
Missing or unreadable images fall back independently to the default artwork.

**Import colony file** loads a separate colony JSON file into that slot.
Generating a map leaves the existing disk save intact until you choose
**Save colony**. Version 1–3 saves remain loadable. Existing buildings retain
their original 1×1 footprints. The first save over a v3 file preserves
`colony.json.pre-v4`. Version 1–2 saves use `colony.json.pre-v3`.

## Inspect, operate, and upgrade

With **Inspect** selected, click a building or any tile under its footprint.
The card shows its actual worker allocation and generated or consumed power
from the simulation, along with configured requirements. **Shut down** removes
the facility from production, power, or staffing. **Enable** returns it to
allocation on the next tick. Shutting down a battery removes its storage
capacity and can discard any stored charge above the remaining capacity.

Mines and wells start at level 1. Inspect them to buy level 2 or 3. Default
upgrades double and triple base yield and require extra crew and power.
Use the build menu and footprint preview to place larger buildings. Area build
fits whole footprints inside the selection. Area demolition removes each
structure intersecting the selection once.

## Replace a building's graphics

1. Find the building ID in `Config/buildings.json`. The iron mine is ID 2.
2. Put the PNG in the appropriate galaxy and environment directory. For
   example, use `wwwroot/assets/galaxies/milky-way/planet/building-2.png`.
3. Add the asset name to that environment's array in the galaxy's
   `artwork.json`, then reload the page.

```json
{ "planet": ["building-2"] }
```

Use a transparent **96×100** canvas for the closest alignment with the existing
sprites. Center the object left to right. The tile diamond starts about halfway
down the image and its front corner is near the bottom. Other sizes are fitted
proportionally and bottom-aligned. One PNG supplies both the map sprite and
construction icon. Multi-tile facilities scale the same art to their footprint.
Put repository artwork under `Source/wwwroot/assets`.

## Change the catalogs

Edit the files under **Source/Config**, then restart. Keep existing building
and material IDs stable so older saves still resolve.

| File | Controls |
|---|---|
| buildings.json | Costs, recipes, crew, power, art, footprints, and upgrades |
| materials.json | Material names, symbols, and starting inventory |
| research.json | Milestones, point thresholds, and prerequisite IDs |
| world.json | Starter layout, deposits and colors, weather, life support, and planetary targets |

Example fields inside a mining definition. Upgrade entries describe the whole
level relative to base output and base requirements.

```json
"footprint": { "width": 2, "height": 1 },
"upgrades": [
  { "multiplier": 2, "cost": { "alloy": 10 }, "extraPower": 2, "extraCrew": 1 },
  { "multiplier": 3, "cost": { "alloy": 18, "silicon": 4 }, "extraPower": 4, "extraCrew": 2 }
]
```

Footprints can be 1–4 tiles wide and tall. Editing a footprint affects future
placements. Existing buildings keep their saved dimensions. Deposit-based
buildings require the correct deposit under their anchor tile. Protect the
starter layout when editing footprints used by the outpost. New mechanics
belong in C# under **Source/Domain**. Existing recipes, footprints, upgrades,
and art can be edited without recompiling.
