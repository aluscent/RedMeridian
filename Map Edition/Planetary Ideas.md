# Planetary buildings and tools

These concepts extend the current colony simulation in stages. Each facility
should make a resource useful or give the player a way to solve a visible problem.

| Facility or tool | What it does | Implementation path |
|---|---|---|
| Regolith kiln and sintering yard | Uses power and dust to make shielding bricks for shelters | Add regolith and bricks, a recipe, and multi-material housing costs |
| Electrolysis stack and gas separator | Splits water into oxygen and hydrogen for reserve life support and fuel synthesis | Add gas materials and recipes. Reserve oxygen can later become a life-support requirement |
| Mycelium recycler | Turns organic waste into nutrients for greenhouses | Add materials and recipes. A colony waste stream would need a population rule |
| Ice radar and coring rover | Reveals underground water and gives the player a way to choose a drilling site | Add survey overlays and a discovery rule keyed by tile |
| Buried cable and pipe trenches | Carry power, water, or fuel between districts | Add networks with district-level flow limits and recompute connectivity on edits |
| Pressure vestibule and sealant drone | Isolates a leaking habitat and patches its seals | Model habitat damage and repair. Start with district aggregates and explicit player actions |
| Storm shelter and dust sweeper | Keeps a district habitable through storms and restores solar output after dust accumulation | Add storm exposure and panel maintenance. A generation bonus alone could be prototyped in config |
| Mass driver and orbital tug terminal | Exchanges processed materials for spare parts or specialist arrivals | Add scheduled contracts and shipment events |
| Mobile fabrication rover | Brings construction supplies to a remote claim and establishes a staging depot | Add limited depot service areas and a construction queue |
| Survey, blueprint, and utility overlays | Preview deposits, count construction materials, and expose power or water bottlenecks | Extend the map tools. Start with deposit and budget overlays using current data |

A practical first batch would be the regolith kiln, electrolysis stack, and
a deposit survey overlay. They extend existing production and map inspection.
Regional utilities, pressure failures, and rover routing would add new
simulation systems and need their own large-colony benchmarks.
