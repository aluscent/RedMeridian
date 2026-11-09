"use strict";

let logistics, cargoInputs;

function setOptions(select, entries) {
  const previous=select.value;
  if(select.options.length===entries.length && entries.every(([value,label],i)=>
    select.options[i].value===String(value) && select.options[i].text===label)) return;
  select.replaceChildren(...entries.map(([value,label])=>new Option(label,value)));
  if(entries.some(([value])=>String(value)===previous)) select.value=previous;
}

function initializeLogistics() {
  cargoInputs=new Map();
  for(const material of catalog.materials.filter(m=>m.id!==catalog.world.researchMaterial)) {
    const label=document.createElement("label"), input=document.createElement("input");
    input.type="number";
    input.min=0;
    input.max=60;
    input.placeholder="0";
    input.addEventListener("input", renderFlightQuote);
    label.append(material.name+" ",input);
    $("cargoInputs").append(label);
    cargoInputs.set(material.id,input);
    $("routeMaterial").add(new Option(material.name,material.id));
  }
  $("logistics").ontoggle=()=>refreshLogistics().catch(error=>notice(error.message,true));
  $("baseSelect").onchange=event=>command("select-base",{base:event.target.value});
  $("assembleShip").onclick=()=>command("assemble");
  $("destination").onchange=$("shipSelect").onchange=renderFlightQuote;
  $("dispatchShip").onsubmit=event=>{
    event.preventDefault();
    command("dispatch",{ship:Number($("shipSelect").value),base:$("destination").value,cargo:flightCargo()});
  };
  $("recurringRoute").onsubmit=event=>{
    event.preventDefault();
    command("set-route",{route:{
      ship:Number($("shipSelect").value),source:state.site.id,destination:$("destination").value,
      material:$("routeMaterial").value,reserve:Number($("routeReserve").value),reorder:Number($("routeReorder").value)
    }});
  };
}

function flightCargo() {
  return Object.fromEntries([...cargoInputs].filter(([,input])=>Number(input.value)>0)
    .map(([id,input])=>[id,Number(input.value)]));
}

function renderFlightQuote() {
  const local=logistics?.bases.find(base=>base.id===state.site.id);
  const destination=$("destination").value;
  const leg=logistics?.legs.find(leg=>leg.from===state.site.id && leg.to===destination);
  const ship=Number($("shipSelect").value);
  const recurring=logistics?.routes.some(route=>route.ship===ship);
  const founding=logistics && !logistics.bases.some(base=>base.id===destination);
  const cargo=flightCargo();
  const units=Object.values(cargo).reduce((sum,amount)=>sum+amount,0);
  const required={...cargo};
  if(leg) required.propellant=(required.propellant||0)+leg.fuel;
  if(founding) required.alloy=(required.alloy||0)+logistics.foundingAlloy;
  const short=local && Object.entries(required).some(([id,amount])=>local.stock[id]<amount);
  $("launchShip").disabled=!leg || !ship || recurring || short || units>logistics.payload || !local?.dock || !local.relay;
  $("setRoute").disabled=!ship || !leg || founding;
  $("flightQuote").textContent=leg
    ? units+"/"+logistics.payload+" cargo · "+leg.fuel+" fuel · "+leg.ticks+" ticks"
      +(founding?" · "+logistics.foundingAlloy+" alloy founding kit":"")
      +(short?" · insufficient stock":"")+(recurring?" · recurring route assigned":"")
    : "Choose a destination for a launch estimate.";
}

async function refreshLogistics() {
  if(!$("logistics").open || !state?.started) return;
  $("logistics").querySelector(".logistics").hidden=state.stress;
  if(state.stress) return;
  const response=await fetch("/api/logistics",{cache:"no-store"});
  if(!response.ok) throw new Error("Logistics data unavailable.");
  logistics=await response.json();
  const names=new Map(logistics.sites.map(site=>[site.id,site.name]));
  const bases=logistics.bases;
  const local=bases.find(base=>base.id===state.site.id);
  if(!local) return;
  setOptions($("baseSelect"),bases.map(base=>[base.id,names.get(base.id)]));
  $("baseSelect").value=state.site.id;
  setOptions($("destination"),logistics.sites.filter(site=>site.id!==local.id)
    .map(site=>[site.id,names.get(site.id)+(bases.some(base=>base.id===site.id)?"":" (found base)")]));
  setOptions($("shipSelect"),logistics.ships.filter(ship=>ship.location===local.id && !ship.destination)
    .map(ship=>[ship.id,"Spacecraft "+ship.id]));

  function row(tag,values) {
    const row=document.createElement("tr");
    for(const value of values) {
      const cell=document.createElement(tag);
      cell.textContent=value;
      row.append(cell);
    }
    return row;
  }
  $("baseHead").replaceChildren(row("th",["Base",...catalog.materials.map(m=>m.name),"Life support runway"]));
  $("baseStock").replaceChildren(...bases.map(base=>row("td",[
    names.get(base.id),...catalog.materials.map(m=>{
      const delta=base.delta[m.id];
      return number.format(base.stock[m.id])+" ("+(delta>=0?"+":"")+delta+"/tick)";
    }),Object.entries(base.reserves).map(([id,ticks])=>id+": "+(ticks===null?"stable":ticks+" ticks")).join(", ")
  ])));
  $("assembleShip").disabled=!local.assembly || !local.dock || local.stock.alloy<logistics.assemblyAlloy;
  $("assembleShip").textContent="Assemble spacecraft · "+logistics.assemblyAlloy+" alloy";
  const orbitalResearch=Math.max(0,...bases.filter(base=>logistics.sites.some(site=>site.id===base.id && site.kind==="orbit"))
    .map(base=>base.stock[catalog.world.researchMaterial]));
  $("spaceFacilities").textContent="Local facilities · assembly "+(local.assembly?"online":"offline")+
    " · cargo "+(local.dock?"online":"offline")+" · relay "+(local.relay?"online":"offline")+
    (orbitalResearch>=30?" · Orbital research objective complete":" · Orbital research objective: "+orbitalResearch+"/30 RP");
  const fleet=$("fleet");
  fleet.replaceChildren(...logistics.ships.map(ship=>{
    const item=document.createElement("li");
    const flight=ship.destination
      ? names.get(ship.location)+" → "+names.get(ship.destination)+" · "+
        (ship.waiting?"Waiting for an operating dock or storage space":"ETA "+Math.max(0,ship.arrivalTick-logistics.tick)+" ticks")+
        " · "+(formatAmounts(ship.cargo || {}) || "empty")
      : "Docked at "+names.get(ship.location);
    item.textContent="Spacecraft "+ship.id+" · "+flight;
    const route=logistics.routes.find(route=>route.ship===ship.id);
    if(route) {
      item.append(" · Recurring "+route.material+" · "+names.get(route.source)+" → "+names.get(route.destination)+" ");
      const clear=document.createElement("button");
      clear.type="button";
      clear.textContent="Clear route";
      clear.onclick=()=>command("clear-route",{ship:ship.id});
      item.append(clear);
    }
    return item;
  }));
  renderFlightQuote();
}
