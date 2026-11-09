"use strict";

const $ = id => document.getElementById(id);
const canvas = $("map"), ctx = canvas.getContext("2d", {alpha:false});
const size = 1024, number = new Intl.NumberFormat();
let catalog, stats, metrics, techCards, tileSprites, buildingSprites, masks, geology;
let surfaceSettings={seed:0,abundance:100,paint:{}}, surfaceRevision=-1;
let widths, heights, owners;

function makeStats(name) {
  const el = document.createElement("div");
  el.className = "stat";
  el.innerHTML = '<small></small><strong>—</strong><small></small>';
  el.firstElementChild.textContent = name;
  $("stats").append(el);
  return el.children;
}
function makeMetric(metric) {
  const el = document.createElement("div");
  el.className = "metric";
  el.innerHTML = '<small></small><output>—</output><progress></progress>';
  el.firstElementChild.textContent = metric.name;
  el.lastElementChild.max = metric.target-metric.initial;
  el.lastElementChild.setAttribute("aria-label", metric.name);
  $("metrics").append(el);
  return el.children;
}

let state, grid, mapRevision = -1, pending = false, refresh, mode = 0, cursor, selected, drag;
let areaMode = false, anchor;
let drawnBuildings = [];
let cx = 511, cy = 511, scale = 1.03, width = 0, height = 0, dpr = 1;
let dirty = true, animation = 0, sweepStart, priorFrame, visible = 0;
const drawTimes = [], frameTimes = [];

function requestDraw() {
  dirty = true;
  if (!animation) animation = requestAnimationFrame(frame);
}

function center() {
  cx = 511;
  cy = 511;
  scale = Math.min(1.07, Math.max(.56, width/850));
  requestDraw();
}
function screen(x,y) {
  return [width/2 + ((x-cx)-(y-cy))*48*scale, height/2 + ((x-cx)+(y-cy))*24*scale];
}
function world(x,y) {
  const dx=(x-width/2)/(96*scale), dy=(y-height/2)/(48*scale);
  return [cx + dx + dy, cy - dx + dy];
}
function groundAt(x,y) {
  const [u,v]=world(x,y);
  return [Math.floor(u),Math.floor(v)];
}
function pick(x,y) {
  if(!areaMode && mode<=0) {
    for(let i=drawnBuildings.length-1;i>=0;i--) {
      const [px,py,type,tx,ty,spriteScale]=drawnBuildings[i];
      const u=Math.floor((x-px)/spriteScale+48), v=Math.floor((y-py)/spriteScale+50);
      if(u>=0 && u<96 && v>=0 && v<100 && masks[type-1][(v*96+u)*4+3]>110) return [tx,ty];
    }
  }
  return groundAt(x,y);
}
function clampCamera() {
  cx = Math.min(size-1, Math.max(0,cx));
  cy = Math.min(size-1, Math.max(0,cy));
}
function zoom(factor,x=width/2,y=height/2) {
  const [oldX,oldY]=world(x,y);
  scale=Math.max(.24, Math.min(2.6,scale*factor));
  const [newX,newY]=world(x,y);
  cx+=oldX-newX;
  cy+=oldY-newY;
  clampCamera();
  requestDraw();
}
function deposit(index) {
  return geology.at(index,surfaceSettings);
}
function footprint(x,y,w,h,color,stroke) {
  Sprites.polygon(ctx,[screen(x,y),screen(x+w,y),screen(x+w,y+h),screen(x,y+h)],color,stroke);
}
function blocked(x,y,recipe) {
  const {width:w,height:h}=recipe.footprint;
  if(x+w>size || y+h>size || (recipe.deposit && deposit(y*size+x)?.id!==recipe.deposit)) return true;
  for(let dy=0;dy<h;dy++) for(let dx=0;dx<w;dx++) if(owners[(y+dy)*size+x+dx]) return true;
  return false;
}
function terrainShade(x,y) {
  const grain = Math.imul(x+19,374761393)^Math.imul(y+13,668265263);
  const ridge = Math.sin(x*.17+y*.10)+Math.sin(y*.21-x*.08);
  return Math.min(7,Math.max(0, Math.floor(4+ridge*1.5+(grain>>>28)/7-1)));
}
function diamond(px,py,color,stroke) {
  const a=48*scale,b=24*scale;
  Sprites.polygon(ctx,[[px,py],[px+a,py+b],[px,py+b*2],[px-a,py+b]],color,stroke);
}
function draw() {
  ctx.setTransform(dpr,0,0,dpr,0,0);
  ctx.fillStyle="#38251d";
  ctx.fillRect(0,0,width,height);
  if(!grid) return;

  const pad=225*scale;
  const corners=[world(-pad,-pad),world(width+pad,-pad),
    world(-pad,height+pad),world(width+pad,height+pad)];
  const x0=Math.max(0,Math.floor(Math.min(...corners.map(p=>p[0]))));
  const x1=Math.min(size-1,Math.ceil(Math.max(...corners.map(p=>p[0]))));
  const y0=Math.max(0,Math.floor(Math.min(...corners.map(p=>p[1]))));
  const y1=Math.min(size-1,Math.ceil(Math.max(...corners.map(p=>p[1]))));
  const buildings=[];
  drawnBuildings=buildings;
  visible=0;
  for(let diagonal=x0+y0;diagonal<=x1+y1;diagonal++) {
    for(let x=Math.max(x0,diagonal-y1);x<=Math.min(x1,diagonal-y0);x++) {
      const y=diagonal-x, [px,py]=screen(x,y);
      if(px < -pad || px > width+pad || py < -pad || py > height+pad) continue;
      const index=y*size+x, type=grid[index];
      const shade=terrainShade(x,y), ore=deposit(index);
      if (px >= -50*scale && px <= width+50*scale && py < height && py+50*scale >= 0) {
        ctx.drawImage(tileSprites[shade+(ore ? ore.index*8 : 0)],px-49*scale,py,98*scale,50*scale);
        if ((state.planet["surface-water"] || 0) >= 10 && !owners[index] && !ore && shade < 2) {
          const wetness = Math.min(.75,.22+(state.planet["surface-water"] || 0)/160);
          diamond(px,py,'rgba(47,119,135,'+wetness+')','#72a6a659');
        }
      }
      if(type) {
        const w=widths[index], h=heights[index];
        const [sx,sy]=screen(x+(w-1)/2,y+(h-1)/2);
        const spriteScale=scale*Math.max(w,h);
        if(sx+48*spriteScale>=0 && sx-48*spriteScale<width && sy+50*spriteScale>=0 && sy-50*spriteScale<height)
          buildings.push([sx,sy,type,x,y,spriteScale,w,h,x+y+w+h]);
      }
    }
  }
  const highlight=cursor || selected;
  if(anchor) {
    const [x,y]=cursor && inside(cursor)?cursor:anchor;
    const x0=Math.min(x,anchor[0]), y0=Math.min(y,anchor[1]);
    const x1=Math.max(x,anchor[0])+1, y1=Math.max(y,anchor[1])+1;
    const warning=mode===-1 || (x1-x0)*(y1-y0)>256;
    Sprites.polygon(ctx,[screen(x0,y0),screen(x1,y0),screen(x1,y1),screen(x0,y1)],
      warning?"#ed795d50":"#ffce7448",warning?"#ff9c81":"#ffd09e");
  } else if(highlight && inside(highlight)) {
    const [x,y]=highlight;
    const recipe=catalog.buildings[mode-1];
    const invalid=recipe && blocked(x,y,recipe);
    const owner=owners[y*size+x]-1;
    if(recipe) footprint(x,y,recipe.footprint.width,recipe.footprint.height,invalid?"#fb77554c":"#ffdfa941",invalid?"#ff9c81":"#ffd09e");
    else if(owner<0) footprint(x,y,1,1,"#ffdfa941","#ffd09e");
    else footprint(owner%size,Math.floor(owner/size),widths[owner],heights[owner],"#ffdfa941","#ffd09e");
  }
  buildings.sort((a,b)=>a[8]-b[8] || a[3]-b[3]);
  const selectedOwner=highlight && inside(highlight) ? owners[highlight[1]*size+highlight[0]]-1 : -1;
  for(const [px,py,type,x,y,spriteScale,w,h] of buildings) {
    if(w>1 || h>1) footprint(x,y,w,h,"#30252280","#bd916b");
    ctx.drawImage(buildingSprites[type-1],px-48*spriteScale,py-50*spriteScale,96*spriteScale,100*spriteScale);

    if(!anchor && selectedOwner===y*size+x && mode<=0) footprint(x,y,w,h,"#ffffff00","#ffd09e");
    visible++;
  }
  if (state.storm) {
    ctx.fillStyle="#e9954033";
    ctx.fillRect(0,0,width,height);
  }
}
function record(samples,value) {
  samples.push(value);
  if(samples.length>180) samples.shift();
}
function p95(samples) {
  if(!samples.length) return "—";
  const sorted=samples.slice().sort((a,b)=>a-b);
  return sorted[Math.ceil(sorted.length*.95)-1].toFixed(2)+" ms";
}
function frame(time) {
  animation=0;
  if(sweepStart!==undefined) {
    if(sweepStart===null) sweepStart=time;
    if(priorFrame!==undefined) record(frameTimes,time-priorFrame);
    priorFrame=time;
    const t=(time-sweepStart)/1000;
    if(t<6) {
      cx=511+Math.sin(t*2)*35;
      cy=511+Math.sin(t*1.3)*28;
      requestDraw();
    } else {
      sweepStart=undefined;
      $("sweep").disabled=false;
      $("frames").textContent=p95(frameTimes);
    }
  }
  if(dirty) {
    const start=performance.now();
    draw();
    placeInfo();
    record(drawTimes,performance.now()-start);
    $("draw").textContent=p95(drawTimes);
    $("count").textContent=state?number.format(state.total)+" / "+number.format(visible):"—";
    dirty=false;
  }
}
new ResizeObserver(([entry])=>{
  const first=!width;
  ({width,height}=entry.contentRect);
  dpr=Math.min(devicePixelRatio || 1,2);
  canvas.width=Math.round(width*dpr);
  canvas.height=Math.round(height*dpr);
  if(first) center();
  requestDraw();
}).observe(canvas);

function chooseMode(value) {
  mode=value;
  if(mode!==0) $("buildingInfo").hidden=true;
  anchor=undefined;
  if(mode===0) setArea(false);
  $("inspectMode").setAttribute("aria-pressed",mode===0);
  $("salvage").setAttribute("aria-pressed",mode===-1);
  Array.from($("builds").children).forEach((button,i)=>button.setAttribute("aria-pressed",mode===i+1));
  if(selected && state) inspectTile(selected);
  requestDraw();
}
function setArea(enabled) {
  areaMode=enabled;
  anchor=undefined;
  $("area").setAttribute("aria-pressed",enabled);
  if(enabled) notice(mode===0?"Choose a building or Demolish, then select two corners.":"Select the first corner.");
  requestDraw();
}
function formatAmounts(amounts) {
  return Object.entries(amounts).map(([id,value])=>value+" "+catalog.materials.find(m=>m.id===id).symbol).join(" + ");
}
function buildingDetail(recipe) {
  const input=formatAmounts(recipe.inputs), output=formatAmounts(recipe.outputs);
  const effects=Object.entries(recipe.effects).map(([id,value])=>{
    const metric=catalog.world.metrics.find(m=>m.id===id);
    return "+"+value/10+metric.unit+" "+metric.name.toLowerCase();
  }).join(", ");
  return [
    recipe.generation && "+"+recipe.generation+" power"+(recipe.stormGeneration!==null?" / "+recipe.stormGeneration+" in storms":""),
    recipe.storage && recipe.storage+" charge · +"+recipe.chargeRate+" / −"+recipe.dischargeRate+" per tick",
    (recipe.footprint.width>1 || recipe.footprint.height>1) && recipe.footprint.width+"×"+recipe.footprint.height+" tiles",
    recipe.beds && recipe.beds+" beds",
    input || output || effects ? [input,[output,effects].filter(Boolean).join(" + ")].filter(Boolean).join(" → ") : "",
    recipe.power && recipe.power+" power",
    recipe.crew && recipe.crew+" crew"
  ].filter(Boolean).join(" · ");
}
function populateMenu() {
  const categories=[...new Set(catalog.buildings.map(b=>b.category))];
  for(const category of categories) $("category").add(new Option(category,category));
  catalog.buildings.forEach((recipe,i)=>{
    const button=document.createElement("button");
    button.className="build";
    button.innerHTML='<img alt=""><span><strong></strong><small></small></span><span class="cost"></span>';
    button.children[0].src=buildingSprites[i].toDataURL();
    button.querySelector("strong").textContent=recipe.name;
    button.querySelector("small").textContent=buildingDetail(recipe);
    button.lastElementChild.textContent=formatAmounts(recipe.cost) || "Free";
    const tech=catalog.research.find(t=>t.id===recipe.research);
    const site=catalog.world.deposits.find(d=>d.id===recipe.deposit);
    button.title=[recipe.detail || buildingDetail(recipe),site?.name,tech && "Research: "+tech.name].filter(Boolean).join(" · ");
    button.onclick=()=>chooseMode(i+1);
    $("builds").append(button);
  });
  $("category").onchange=()=>{
    const category=$("category").value;
    Array.from($("builds").children).forEach((button,i)=>{
      button.hidden=Boolean(category && category!==catalog.buildings[i].category);
    });
    if(mode>0 && category && catalog.buildings[mode-1].category!==category) chooseMode(0);
  };
  chooseMode(0);
}
function display(next) {
  const redraw=!state || state.storm!==next.storm || state.planet["surface-water"]!==next.planet["surface-water"];
  state=next;
  document.body.classList.toggle("playing",next.started);
  $("loadSaved").disabled=!next.hasSave;
  const entries=catalog.materials.map(m=>{
    const delta=next.delta[m.id];
    return [number.format(next.stock[m.id]),(delta>0?"+":"")+number.format(delta)+" / tick",delta];
  });
  entries.push(
    [number.format(next.supply + next.batteryAvailable - next.demand),
      number.format(next.supply)+" gen / "+number.format(next.demand)+" need"],
    [number.format(next.batteryCharge)+" / "+number.format(next.batteryCapacity),"Stored power"],
    [next.population+" / "+next.housing,next.crewUsed+" / "+next.crewCapacity+" crew slots"]
  );
  entries.forEach(([value,detail,delta],i)=>{
    stats[i][1].textContent=value;
    stats[i][2].textContent=detail;
    stats[i][2].className=delta>0?"positive":delta<0?"negative":"";
  });
  catalog.world.metrics.forEach((metric,i)=>{
    const value=next.planet[metric.id];
    metrics[i][1].textContent=(value/10).toFixed(1)+metric.unit+" / "+metric.target/10+metric.unit;
    metrics[i][2].value=value-metric.initial;
  });
  const unlocked=new Set(next.unlocked);
  catalog.research.forEach((t,i)=>{
    techCards[i].classList.toggle("positive",unlocked.has(t.id));
    techCards[i].lastElementChild.textContent=unlocked.has(t.id)?"Unlocked":
      t.points+" RP"+(t.requires.length?" · "+t.requires.map(id=>catalog.research.find(other=>other.id===id).name).join(", "):"");
  });
  Array.from($("builds").children).forEach((button,i)=>{
    const id=catalog.buildings[i].research;
    const locked=Boolean(id && !unlocked.has(id));
    button.classList.toggle("locked",locked);
    button.querySelector("strong").textContent=(locked?"🔒 ":"")+catalog.buildings[i].name;
  });
  $("pause").textContent=next.paused?"Resume":"Pause";
  $("speed").textContent=next.speed+"× speed";
  $("save").disabled=next.stress;
  $("weather").hidden=!next.storm;
  $("world").textContent=next.stress?"Benchmark sector":"Landing sector";
  $("clock").textContent="Sol "+(Math.floor(next.tick/24)+1)+" · "+String(next.tick%24).padStart(2,"0")+":00";
  $("mission").textContent=next.complete?"Terraforming targets met":next.shortage?"Life support shortage":
    next.unlocked.length===0?"Build a lab to unlock new facilities":"Research and terraforming underway";
  $("tick").textContent=next.tickMs.toFixed(3)+" ms";
  $("stress").textContent=next.stress?"Return to colony":"Load 100k stress map";
  $("sweep").hidden=!next.stress;
  if(redraw) requestDraw();
}
function setOwner(index,w,h,owner) {
  for(let y=0;y<h;y++) for(let x=0;x<w;x++) owners[index+y*size+x]=owner;
}
function applyTileEdit(edit) {
  const index=edit.index;
  if(grid[index]) setOwner(index,widths[index],heights[index],0);
  grid[index]=edit.type;
  widths[index]=edit.type?edit.width:0;
  heights[index]=edit.type?edit.height:0;
  if(edit.type) setOwner(index,edit.width,edit.height,index+1);
}
async function syncMap() {
  if(surfaceRevision!==state.worldRevision) {
    const terrain=await fetch("/api/surface",{cache:"no-store"});
    if(!terrain.ok) throw new Error("Could not load geology.");
    surfaceSettings=await terrain.json();
    surfaceRevision=state.worldRevision;
  }
  const response=await fetch("/api/map",{cache:"no-store"});
  if(!response.ok) throw new Error("Could not load the sector map.");
  const cells=new Uint8Array(await response.arrayBuffer()), area=size*size;
  if(cells.length!==area*3) throw new Error("Incomplete sector map.");
  grid=cells.subarray(0,area);
  widths=cells.subarray(area,2*area);
  heights=cells.subarray(2*area);
  owners=new Uint32Array(area);
  for(let index=0;index<area;index++) if(grid[index]) setOwner(index,widths[index],heights[index],index+1);
  mapRevision=Number(response.headers.get("X-Map-Revision"));
  requestDraw();
}
function notice(message,error=false) {
  $("notice").textContent=message;
  if(!state?.started) $("menuNotice").textContent=message;
  $("notice").classList.toggle("error",error);
}
async function command(action,extra={}) {
  if(pending || !state) return;
  pending=true;
  try {
    await refresh;
    const previous=mapRevision;
    const response=await fetch("/api/command",{
      method:"POST",headers:{"Content-Type":"application/json"},
      body:JSON.stringify({action,revision:previous,...extra})
    });
    const result=await response.json();
    if(!result.state) throw new Error(result.message || "Command failed.");
    display(result.state);
    if(response.ok && grid && result.edits && result.state.revision===previous+1) {
      for(const edit of result.edits) applyTileEdit(edit);
      mapRevision=result.state.revision;
      requestDraw();
    }
    if(state.started && (state.revision!==mapRevision || surfaceRevision!==state.worldRevision)) await syncMap();
    if(["stress","new","load","import","menu"].includes(action) && response.ok) {
      sweepStart=undefined;
      $("sweep").disabled=false;
      frameTimes.length=drawTimes.length=0;
      $("frames").textContent="—";
      selected=cursor=anchor=undefined;
      $("buildingInfo").hidden=true;
      $("inspect").textContent="Select a tile to inspect it.";
      center();
    }
    if(selected) inspectTile(selected);
    notice(result.message,!response.ok);
  } catch(error) {
    notice(error.message,true);
  } finally {
    pending=false;
  }
}
async function refreshState() {
  try {
    const inspect=selected && mode===0 ? "?inspect="+(selected[1]*size+selected[0]) : "";
    const response=await fetch("/api/state"+inspect,{cache:"no-store"});
    if(!response.ok) throw new Error("Colony server unavailable.");
    display(await response.json());
    if(state.started && (state.revision!==mapRevision || surfaceRevision!==state.worldRevision)) await syncMap();
    if(selected && state.started) inspectTile(selected);
    updateInfo(state.inspected);
  } catch {
    notice("Colony server unavailable. Retrying…",true);
  }
}
async function poll() {
  if(!pending) {
    refresh=refreshState();
    await refresh;
    refresh=undefined;
  }
  setTimeout(poll,650);
}
const inside=([x,y])=>x>=0&&y>=0&&x<size&&y<size;
function inspectTile([x,y]) {
  if(!grid || !inside([x,y])) return;
  const index=y*size+x, owner=owners[index]-1, resource=deposit(index);
  const recipe=owner>=0?catalog.buildings[grid[owner]-1]:null;
  $("inspect").textContent=x+", "+y+"  ·  "+(recipe?.name || resource?.name || "Open regolith");
}
function placeInfo() {
  const box=$("buildingInfo");
  if(box.hidden || !state?.inspected) return;
  const index=state.inspected.index;
  const [px,py]=screen(index%size+(widths[index]-1)/2,Math.floor(index/size)+(heights[index]-1)/2);
  const left=Math.min(width-box.offsetWidth-6,px+28);
  box.style.left=Math.max(6,left)+"px";
  box.style.top=Math.max(6,Math.min(height-box.offsetHeight-6,py-box.offsetHeight*.65))+"px";
}
function updateInfo(info) {
  const box=$("buildingInfo");
  const tile=selected?selected[1]*size+selected[0]:-1;
  if(!info || !state.started || mode!==0 || owners?.[tile]!==info.index+1) {box.hidden=true;return;}
  const recipe=catalog.buildings[info.type-1];
  $("infoName").textContent=recipe.name+(recipe.upgrades.length?" · Level "+info.level:"");
  const lines=[(info.disabled?"Shut down":info.running?"Operating":"Idle")+" · "+info.width+"×"+info.height+" tiles",
    "Workers "+info.workers+" / "+info.crew];
  if(info.ratedPowerOutput) lines.push("Power output "+info.powerOutput+" / "+info.ratedPowerOutput);
  if(info.ratedPowerUse) lines.push("Power draw "+info.powerUse+" / "+info.ratedPowerUse);
  if(Object.keys(info.outputs).length) lines.push("Yield "+formatAmounts(info.outputs)+" / operating tick");
  if(recipe.forecastsWeather) {
    const forecast=info.weatherForecast;
    if(!forecast) lines.push("Forecast pending. Requires power and crew.");
    else {
      const age=state.tick-forecast.observedAt;
      const range=(min,max)=>min===max ? String(min) : min+"–"+max;
      lines.push(age || !info.running
        ? "Stale forecast · recorded at tick "+forecast.observedAt+" ("+age+" ticks ago)"
        : "Forecast recorded at tick "+forecast.observedAt);
      lines.push("Next storm: "+range(forecast.startsInMin,forecast.startsInMax)+" ticks from reading");
      lines.push("Expected duration: "+range(forecast.durationMin,forecast.durationMax)+" ticks");
      lines.push("Storm solar: "+forecast.stormSolarOutput+" / "+forecast.demand+" total demand");
      lines.push(forecast.batteryTicks===null ? "Battery backup unnecessary at recorded demand"
        : "Estimated battery cover: "+forecast.batteryTicks+" full ticks");
    }
  }
  $("infoDetails").textContent=lines.join("\n");
  $("toggleBuilding").textContent=info.disabled?"Enable":"Shut down";
  $("toggleBuilding").onclick=()=>command("toggle",{index:info.index});
  $("upgradeBuilding").hidden=!info.upgrade;
  if(info.upgrade) {
    $("upgradeBuilding").textContent="Upgrade · "+(formatAmounts(info.upgrade.cost)||"Free");
    $("upgradeBuilding").title=info.upgrade.multiplier+"× base yield · +"+info.upgrade.extraPower+" power · +"+info.upgrade.extraCrew+" crew";
    $("upgradeBuilding").onclick=()=>command("upgrade",{index:info.index});
  }
  box.hidden=false;
  placeInfo();
}
function activate(tile) {
  if(!grid || pending || !inside(tile)) return;
  selected=tile;
  $("buildingInfo").hidden=true;
  inspectTile(tile);
  const [x,y]=tile, index=y*size+x;
  if(mode!==0 && areaMode) {
    if(!anchor) {
      anchor=tile;
      notice("Select the opposite corner.");
    } else {
      const start=anchor[1]*size+anchor[0];
      anchor=undefined;
      command("area",{index:start,endIndex:index,type:mode===-1?0:mode});
    }
  } else if(mode!==0) command(mode===-1?"remove":"build",{index,type:mode});
  requestDraw();
}
function local(event) {
  const rect=canvas.getBoundingClientRect();
  return [event.clientX-rect.left,event.clientY-rect.top];
}
canvas.addEventListener("pointerdown",event=>{
  if(event.button!==0 || drag) return;
  canvas.focus({preventScroll:true});
  canvas.setPointerCapture(event.pointerId);
  drag={id:event.pointerId,start:local(event),cx,cy,moved:false};
});
canvas.addEventListener("pointermove",event=>{
  if(drag && drag.id!==event.pointerId) return;
  const [x,y]=local(event);
  if(drag) {
    const dx=x-drag.start[0], dy=y-drag.start[1];
    drag.moved ||= Math.hypot(dx,dy)>4;
    if(drag.moved) {
      cx=drag.cx-dx/(96*scale)-dy/(48*scale);
      cy=drag.cy+dx/(96*scale)-dy/(48*scale);
      clampCamera();
    }
  }
  cursor=drag?.moved?groundAt(x,y):pick(x,y);
  requestDraw();
});
canvas.addEventListener("pointerup",event=>{
  if(drag?.id!==event.pointerId) return;
  if(!drag.moved) activate(pick(...local(event)));
  drag=undefined;
});
canvas.addEventListener("lostpointercapture",()=>{drag=undefined;});
canvas.addEventListener("pointerleave",()=>{cursor=undefined;requestDraw();});
canvas.addEventListener("wheel",event=>{
  event.preventDefault();
  zoom(Math.exp(-Math.sign(event.deltaY)*.15),...local(event));
},{passive:false});
canvas.addEventListener("keydown",event=>{
  const offsets={ArrowUp:[0,-1],ArrowDown:[0,1],ArrowLeft:[-1,0],ArrowRight:[1,0]};
  const tile=cursor||selected||[Math.floor(cx),Math.floor(cy)];
  if(event.key==="Enter") {
    event.preventDefault();
    activate(tile);
  } else if(offsets[event.key]) {
    event.preventDefault();
    const [dx,dy]=offsets[event.key];
    const next=[tile[0]+dx,tile[1]+dy];
    if(inside(next)) {
      cursor=next;
      const [px,py]=screen(...next);
      if(px<45 || px>width-45 || py<45 || py>height-45) [cx,cy]=next;
      requestDraw();
    }
  }
});
document.addEventListener("keydown",event=>{if(event.key==="Escape") chooseMode(0);});
$("home").onclick=center;
$("in").onclick=()=>zoom(1.2);
$("out").onclick=()=>zoom(1/1.2);
$("inspectMode").onclick=()=>chooseMode(0);
$("salvage").onclick=()=>chooseMode(-1);
$("area").onclick=()=>setArea(!areaMode);
for(const action of ["save","pause","speed","stress"]) $(action).onclick=()=>command(action);
$("sweep").onclick=()=>{
  $("sweep").disabled=true;
  frameTimes.length=drawTimes.length=0;
  priorFrame=undefined;
  sweepStart=null;
  requestDraw();
};
$("closeInfo").onclick=()=>{
  selected=undefined;
  $("buildingInfo").hidden=true;
  requestDraw();
};
$("mainMenu").onclick=()=>{if(confirm("Return to the main menu? Unsaved progress will be discarded.")) command("menu");};
$("loadSaved").onclick=()=>command("load");
$("importFromMenu").onclick=$("import").onclick=()=>$("file").click();
$("file").onchange=async event=>{
  const [file]=event.target.files;
  event.target.value="";
  if(!file || !confirm("Import this colony and replace the save on disk?")) return;
  try {
    if(file.size>30_000_000) throw new Error("Save file is too large.");
    await command("import",{save:JSON.parse(await file.text())});
  } catch(error) { notice(error.message,true); }
};
async function loadSprite(building) {
  const template=Sprites.buildings[Math.max(0,Sprites.art.indexOf(building.art))];
  if(!building.image) return template;
  const image=new Image();
  image.src=building.image+"?v=map-4";
  try { await image.decode(); }
  catch { console.warn("Could not load building PNG:",building.image);return template; }
  const sprite=Object.assign(document.createElement("canvas"),{width:96,height:100});
  const fit=Math.min(96/image.naturalWidth,100/image.naturalHeight);
  const w=image.naturalWidth*fit,h=image.naturalHeight*fit;
  sprite.getContext("2d").drawImage(image,(96-w)/2,100-h,w,h);
  return sprite;
}
async function initialize() {
  try {
    const response=await fetch("/api/catalog",{cache:"no-store"});
    if(!response.ok) throw new Error("Catalog unavailable");
    catalog=await response.json();
    stats=[...catalog.materials.map(m=>m.name),"Power margin","Battery","Colonists"].map(makeStats);
    metrics=catalog.world.metrics.map(makeMetric);
    techCards=catalog.research.map(t=>{
      const card=document.createElement("div");
      card.textContent=t.name;
      card.append(document.createElement("small"));
      $("research").append(card);
      return card;
    });
    const deposits=catalog.world.deposits.map((d,index)=>({...d,index:index+1}));
    [buildingSprites,tileSprites]=await Promise.all([
      Promise.all(catalog.buildings.map(loadSprite)),
      Sprites.makeTiles(deposits)
    ]);
    masks=buildingSprites.map(sprite=>sprite.getContext("2d").getImageData(0,0,96,100).data);
    deposits.forEach(d=>{
      const key=document.createElement("span");
      key.style.color=d.colors[0];
      key.textContent="◆ "+d.name;
      $("deposits").append(key);
    });
    geology=makeGeology(catalog,deposits);
    createMapEditor(catalog,deposits,geology,map=>command("new",{map}));
    populateMenu();
    poll();
  } catch(error) {
    notice("Could not load the game catalog. "+error.message,true);
  }
}
initialize();

