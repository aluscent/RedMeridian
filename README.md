# Red Meridian — Map Edition

New Mars maps have 10,000 tiles, and Venus outposts have 13,440. Adjust their
dimensions under `planetSizes` in `Config/world.json` within the 8,000–14,000
tile range. Existing colonies retain their saved bounds, and tester mode
still offers the 100,000-structure stress map.

Run **Start Red Meridian.cmd** and open http://127.0.0.1:5287/?v=map-4.
Keep the server window open. The Windows build requires the ASP.NET Core 8 runtime.
To build from source with the .NET 8 SDK, run
`dotnet run --project Source -- --urls http://127.0.0.1:5287`.

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
   24 alloy and installs solar power, a habitat, communications, and a dock or
   terminal. Pack food, water, alloy for new facilities, and return propellant.
3. After arrival, switch views from the same panel. Build a Research Lab at an
   orbital station and reach 30 RP there. Ceres offers iron, titanium, and
   uranium. Europa offers ice, brine, and silicon, with weaker solar output.
4. For an established destination, schedule a ship with a material, source
   reserve, and destination reorder threshold. It makes repeat trips as
   supplies and fuel allow. Leave return propellant at the destination.

All bases advance together. Cargo remains aboard if the destination's dock is
offline or storage is full. Saving captures all bases, spacecraft, and routes.
Older single-colony saves open on Mars with an empty fleet.
New orbital stations are 5 tiles wide and 150 tiles long. Existing orbital
stations keep their saved layouts.

## Activate research milestones

Research Labs generate research points. Open **Research milestones** and click
**Activate** when the colony has enough points and its prerequisites are active.
Activation uses the selected base's points as a threshold and applies to all
bases. Older saves retain the combined milestones unlocked across their bases.

Edit `Play/Config/spacecraft.json` and mirror changes in `Source/Config` to
configure spacecraft types. Each entry has an `id`, `name`, `payload`,
`fuelPercent`, `buildTicks`, and a `cost` object keyed by material ID. Fuel is
rounded up after applying the percentage to the route's standard fuel cost.
Keep IDs stable because spacecraft and assembly orders reference them in saves.

## Tester mode

Run **Start Red Meridian Tester.cmd** and open http://127.0.0.1:5288/.
The **Tester tools** section contains resource grants, manual tick advancement,
the 100,000-structure stress map, and its pan test. Tester mode defaults to
`colony.test.json` in the save directory, while normal play uses `colony.json`.

## Generate, edit, or load a map

The launch screen previews the geology before the colony clock starts.
Enter a signed 32-bit seed or roll one. The same seed, abundance, and paint
produce the same deposit layout. Abundance controls deposit density from
0% to 200% of the default distribution. Deposits are continuous sources.
The starter iron and ice deposits are protected at every abundance setting.

Open **Map editor** to paint any configured deposit, clear it, or restore the
generated tile. Pan with the arrow buttons. The light outlines mark the
starting outpost. **Export map** and **Import map** share terrain designs as
JSON. A map design contains a seed, abundance, and paint overrides.

**Load saved colony** reads `%LOCALAPPDATA%\RedMeridian\colony.json`.
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

1. Put a transparent PNG in **Play/wwwroot/assets**. For example, name it
   `iron-mine.png`. Letters, numbers, hyphens, and underscores are supported.
2. In **Play/Config/buildings.json**, find the building by its name or ID. Add
   an `image` property and keep its existing `art` property as a fallback.
3. Restart the server and reload the page. A missing or invalid PNG falls
   back to the built-in art.

The field looks like this inside the building's existing JSON object.

```json
"image": "assets/iron-mine.png"
```

Use a transparent **96×100** canvas for the closest alignment with the existing
sprites. Center the object left to right. The tile diamond starts about halfway
down the image and its front corner is near the bottom. Other sizes are fitted
proportionally and bottom-aligned. One PNG supplies both the map sprite and
construction icon. Multi-tile facilities scale the same art to their footprint.
Copy your PNG to **Source/wwwroot/assets** too if you build from source.

## Change the catalogs

Edit the live files under **Play/Config**, then restart. Make corresponding
changes under **Source/Config** when rebuilding. Keep existing building and
material IDs stable so older saves still resolve.

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
