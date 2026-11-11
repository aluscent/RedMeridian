"use strict";

let logistics, cargoInputs;
let starZoom=0;
const baseRows=new Map(), fleetRows=new Map();

function pruneRows(rows,ids) {
  const keep=new Set(ids);
  for(const [id,row] of rows) {
    if(keep.has(id)) continue;
    row.remove();
    rows.delete(id);
  }
}

function setOptions(select, entries) {
  const previous=select.value;
  if(select.options.length===entries.length && entries.every(([value,label],i)=>
    select.options[i].value===String(value) && select.options[i].text===label)) return;
  select.replaceChildren(...entries.map(([value,label])=>new Option(label,value)));
  if(entries.some(([value])=>String(value)===previous)) select.value=previous;
}

function initializeLogistics() {
  cargoInputs=new Map();
  const header=document.createElement("tr");
  for(const name of ["Base",...catalog.materials.map(material=>material.name),"Life support runway"]) {
    const cell=document.createElement("th");
    cell.textContent=name;
    header.append(cell);
  }
  $("baseHead").append(header);
  for(const material of catalog.materials.filter(m=>m.id!==catalog.world.researchMaterial)) {
    const label=document.createElement("label"), input=document.createElement("input");
    input.type="number";
    input.min=0;
    input.max=Math.max(...catalog.shipTypes.map(type=>type.payload));
    input.placeholder="0";
    input.addEventListener("input", renderFlightQuote);
    label.append(material.name+" ",input);
    $("cargoInputs").append(label);
    cargoInputs.set(material.id,input);
    $("routeMaterial").add(new Option(material.name,material.id));
  }
  $("baseSelect").onchange=event=>command("select-base",{base:event.target.value});
  $("zoomStarsIn").onclick=()=>changeStarZoom(1);
  $("zoomStarsOut").onclick=()=>changeStarZoom(-1);
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
  const type=catalog.shipTypes.find(type=>type.id===logistics?.ships.find(item=>item.id===ship)?.type);
  const flight=leg?.flights.find(flight=>flight.type===type?.id);
  const fuel=flight?.fuel ?? 0;
  const recurring=logistics?.routes.some(route=>route.ship===ship);
  const founding=logistics && !logistics.bases.some(base=>base.id===destination);
  const cargo=flightCargo();
  const units=Object.values(cargo).reduce((sum,amount)=>sum+amount,0);
  const required={...cargo};
  if(leg) required.propellant=(required.propellant||0)+fuel;
  if(founding) required.alloy=(required.alloy||0)+logistics.foundingAlloy;
  const short=local && Object.entries(required).some(([id,amount])=>local.stock[id]<amount);
  setProperty($("launchShip"),"disabled",Boolean(!flight || recurring || short || units>type.payload || !local?.dock || !local.relay));
  setProperty($("setRoute"),"disabled",Boolean(!ship || !flight || founding));
  setText($("flightQuote"),flight
    ? number.format(leg.distance)+" UoD · "+units+"/"+type.payload+" cargo · "+number.format(fuel)+" fuel · "+number.format(flight.ticks)+" ticks"
      +(founding?" · "+logistics.foundingAlloy+" alloy founding kit":"")
      +(short?" · insufficient stock":"")+(recurring?" · recurring route assigned":"")
    : leg && type ? number.format(leg.distance)+" UoD exceeds this spacecraft's "+number.format(type.range)+" UoD range."
    : "Choose an idle spacecraft and a destination. Build spacecraft from the assembly yard inspector.");
}

function updateExploration(telescope,index) {
  const dialog=$("explorationDialog");
  $("openExploration").onclick=()=>dialog.showModal();
  const {level,range,online,survey}=telescope;
  const stock=state.stock[catalog.world.researchMaterial];
  let status="Level "+level+" · "+number.format(range)+" UoD range · "+number.format(stock)+" RP in stock";
  if(survey) status+=" · "+(survey.galaxy?"Galaxy":"Star system")+" survey: "+
    Math.ceil(survey.ticksLeft/24)+" sols remaining"+(online?"":" (paused)");
  else if(!online) status+=" · Requires power and crew";
  else if(!telescope.systems.available && !telescope.galaxies.available) status+=" · No undiscovered targets in range";
  if(level<3) status+=" · Galaxy surveys require level 3";
  setText($("explorationStatus"),status);
  for(const [id,option,galaxy] of [["surveySystems",telescope.systems,false],["surveyGalaxies",telescope.galaxies,true]]) {
    const button=$(id);
    setText(button,(galaxy?"Survey galaxies":"Survey star systems")+" · "+option.sols+" sols · "+number.format(option.cost)+" RP");
    setProperty(button,"disabled",Boolean(survey || !online || !option.available || stock<option.cost));
    button.onclick=()=>command("explore",{index,galaxySurvey:galaxy});
  }
}

function changeStarZoom(delta) {
  starZoom=Math.max(0,Math.min(5,starZoom+delta));
  drawStarChart();
}

function drawStarChart() {
  const origin=logistics?.systems.find(system=>system.id===state.site.system);
  if(!origin) return;
  const canvas=$("starChart"), g=canvas.getContext("2d");
  const x=canvas.width/2, y=canvas.height/2, radius=y-16;
  const radial=distance=>Math.log10(1+distance)*radius/(6-starZoom);
  g.clearRect(0,0,canvas.width,canvas.height);
  g.font="10px system-ui";
  g.textAlign="left";
  g.textBaseline="middle";
  for(const distance of [1,10,100,1000,10000,100000]) {
    const r=radial(distance);
    if(r>radius) break;
    g.strokeStyle="#42536a";
    g.beginPath();g.arc(x,y,r,0,Math.PI*2);g.stroke();
    g.fillStyle="#9caabd";
    g.fillText(number.format(distance),x+3,y-r-5);
  }
  const marks=[];
  function mark(name,distance,angle,color,glyph) {
    const r=radial(distance);
    if(r>radius) return;
    g.fillStyle=color;
    g.textAlign="center";
    g.font=(glyph==="◉"?"15":"12")+"px system-ui";
    const px=x+Math.cos(angle)*r, py=y+Math.sin(angle)*r;
    g.fillText(glyph,px,py);
    marks.push({px,py,label:name+" · "+number.format(Math.ceil(distance))+" UoD"});
  }
  const planets=logistics.sites.filter(site=>site.system===origin.id && site.planetaryEngineering);
  planets.forEach((site,i)=>mark(site.name,Math.max(site.distance,.6),i*2.4,"#80e4db","○"));
  for(const star of logistics.systems.filter(system=>system.galaxy===origin.galaxy && system.id!==origin.id))
    mark(star.name,Math.hypot(star.x-origin.x,star.y-origin.y),Math.atan2(star.y-origin.y,star.x-origin.x),"#ffd885","◇");
  for(const galaxy of logistics.galaxies.filter(galaxy=>galaxy.id!==origin.galaxy))
    mark(galaxy.name,Math.hypot(galaxy.x-origin.x,galaxy.y-origin.y),Math.atan2(galaxy.y-origin.y,galaxy.x-origin.x),"#d3adff","◉");
  mark(origin.name,0,0,"#ffe2a2","✦");
  canvas.onmousemove=event=>{
    const px=event.offsetX*canvas.width/canvas.clientWidth, py=event.offsetY*canvas.height/canvas.clientHeight;
    const match=marks.filter(mark=>Math.hypot(mark.px-px,mark.py-py)<8);
    setProperty(canvas,"title",match.map(mark=>mark.label).join("\n"));
  };
  setText($("travelMapOrigin"),origin.name);
  setProperty($("zoomStarsIn"),"disabled",starZoom===5);
  setProperty($("zoomStarsOut"),"disabled",starZoom===0);
}

function setList(list,values,empty) {
  if(values.length===0 && empty) values=[empty];
  if(list.children.length===values.length && values.every((value,index)=>list.children[index].textContent===value)) return;
  list.replaceChildren(...values.map(value=>{
    const row=document.createElement("li");
    row.textContent=value;
    return row;
  }));
}

async function refreshLogistics() {
  if($("controlPanel").hidden || $("logistics").hidden && $("systemPanel").hidden || !state?.started) return;
  $("logistics").querySelector(".logistics").hidden=state.stress;
  if(state.stress) return;
  const response=await fetch("/api/logistics",{cache:"no-store"});
  if(!response.ok) throw new Error("Logistics data unavailable.");
  logistics=await response.json();
  if($("controlPanel").hidden) return;
  const names=new Map(logistics.sites.map(site=>[site.id,site.name]));
  const systems=new Map(logistics.systems.map(system=>[system.id,system]));
  const origin=systems.get(state.site.system);
  if(!origin) return;
  const distance=site=>{
    if(site.system===origin.id) return site.distance;
    const target=systems.get(site.system);
    return Math.ceil(Math.hypot(target.x-origin.x,target.y-origin.y));
  };
  const destinationLabel=site=>site.name+" · "+systems.get(site.system).name+" · "+number.format(distance(site))+" UoD";
  const mapSlot=$("systemPanel").hidden ? $("travelMapSlot") : $("systemPanel");
  if($("travelMap").parentElement!==mapSlot) mapSlot.prepend($("travelMap"));
  drawStarChart();
  if(!$("systemPanel").hidden) {
    const known=[...logistics.sites].sort((a,b)=>distance(a)-distance(b));
    setList($("systemMap"),[...known.map(destinationLabel),...logistics.galaxies.filter(g=>g.id!==origin.galaxy).map(g=>
      g.name+" · "+number.format(Math.ceil(Math.hypot(g.x-origin.x,g.y-origin.y)))+" UoD")]);
    return;
  }
  const bases=logistics.bases;
  const local=bases.find(base=>base.id===state.site.id);
  if(!local) return;
  setOptions($("baseSelect"),bases.map(base=>[base.id,names.get(base.id)]));
  setProperty($("baseSelect"),"value",state.site.id);
  setOptions($("destination"),logistics.sites.filter(site=>site.id!==local.id)
    .map(site=>[site.id,destinationLabel(site)+(bases.some(base=>base.id===site.id)?"":" (found base)")]));
  setOptions($("shipSelect"),logistics.ships.filter(ship=>ship.location===local.id && !ship.destination)
    .map(ship=>[ship.id,catalog.shipTypes.find(type=>type.id===ship.type).name+" #"+ship.id]));

  pruneRows(baseRows,bases.map(base=>base.id));
  for(const base of bases) {
    const values=[names.get(base.id),...catalog.materials.map(material=>{
      const delta=base.delta[material.id];
      return number.format(base.stock[material.id])+" ("+(delta>=0?"+":"")+delta+"/tick)";
    }),Object.entries(base.reserves).map(([id,ticks])=>id+": "+(ticks===null?"stable":ticks+" ticks")).join(", ")];
    let row=baseRows.get(base.id);
    if(!row) {
      row=document.createElement("tr");
      for(const value of values) row.insertCell().textContent=value;
      baseRows.set(base.id,row);
      $("baseStock").append(row);
    } else values.forEach((value,i)=>setText(row.cells[i],value));
  }
  const orbitalResearch=Math.max(0,...bases.filter(base=>logistics.sites.some(site=>site.id===base.id && site.kind==="orbit"))
    .map(base=>base.stock[catalog.world.researchMaterial]));
  setText($("spaceFacilities"),"Local facilities · assembly "+(local.assembly?"online":"offline")+
    " · cargo "+(local.dock?"online":"offline")+" · relay "+(local.relay?"online":"offline")+
    (orbitalResearch>=30?" · Orbital research objective complete":" · Orbital research objective: "+orbitalResearch+"/30 RP"));
  setList($("assemblyOrders"),bases.flatMap(base=>base.orders.map(order=>{
    const type=catalog.shipTypes.find(item=>item.id===order.type);
    return type.name+" at "+names.get(base.id)+" — "+order.ticksLeft+" ticks"+(order.running?"":" (paused)");
  })), "No spacecraft under construction.");
  pruneRows(fleetRows,logistics.ships.map(ship=>ship.id));
  for(const ship of logistics.ships) {
    let item=fleetRows.get(ship.id);
    if(!item) {
      item=document.createElement("li");
      const label=document.createElement("span"), clear=document.createElement("button");
      clear.type="button";
      clear.textContent="Clear route";
      clear.onclick=()=>command("clear-route",{ship:ship.id});
      item.append(label,clear);
      fleetRows.set(ship.id,item);
      $("fleet").append(item);
    }
    const flight=ship.destination
      ? names.get(ship.location)+" → "+names.get(ship.destination)+" · "+
        (ship.waiting?"Waiting for an operating dock or storage space":"ETA "+Math.max(0,ship.arrivalTick-logistics.tick)+" ticks")+
        " · "+(formatAmounts(ship.cargo || {}) || "empty")
      : "Docked at "+names.get(ship.location);
    const route=logistics.routes.find(route=>route.ship===ship.id);
    const description=route?" · Recurring "+route.material+" · "+names.get(route.source)+" → "+names.get(route.destination)+" ":"";
    setText(item.firstElementChild,catalog.shipTypes.find(type=>type.id===ship.type).name+" #"+ship.id+" · "+flight+description);
    setProperty(item.lastElementChild,"hidden",!route);
  }
  renderFlightQuote();
}
