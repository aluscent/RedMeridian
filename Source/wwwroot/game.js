"use strict";

const $ = id => document.getElementById(id);
const canvas = $("map"), ctx = canvas.getContext("2d", {alpha:false});
const size = 1024, number = new Intl.NumberFormat();
let catalog, stats, metrics, techCards, tileSprites, buildingSprites, masks, geology;
let randomizeCampaignSeed;
let surfaceSettings={seed:0,abundance:100,paint:{}}, surfaceRevision=-1;
let climateData, climateOverlay="terrain", climateFormKey="";
const waterNames=["Land","Frozen ocean","Liquid ocean","Dry seabed"];
let widths, heights, owners, mapSite, mapWorldRevision=-1;
const environmentSprites=new Map();
let spriteEnvironment;
const statusResources=[
  {id:"power-margin",name:"Power margin",symbol:"⚡"},
  {id:"battery",name:"Battery",symbol:"▣"},
  {id:"colonists",name:"Colonists",symbol:"♟"}
];

function makeStats(name,icon) {
  const el = document.createElement("details");
  el.className = "stat";
  el.innerHTML = '<summary><span aria-hidden="true"></span> <strong>-</strong></summary><small></small>';
  const [summary,detail]=el.children;
  summary.firstElementChild.textContent=icon;
  summary.title=name;
  $("stats").append(el);
  return [summary,summary.lastElementChild,detail,name,summary.firstElementChild];
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
let nextDrawReport=0;
const drawTimes = [], frameTimes = [];

function requestDraw() {
  dirty = true;
  if (!animation) animation = requestAnimationFrame(frame);
}

function center() {
  cx = state ? state.site.left+(state.site.width-1)/2 : 511;
  cy = state ? state.site.top+(state.site.height-1)/2 : 511;
  scale = Math.min(1.07, Math.max(.56, width/850));
  requestDraw();
}
function screen(x,y) {
  return Isometric.project(x-cx,y-cy,scale,width/2,height/2);
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
  if(!state) return;
  const {left,top,width:spanX,height:spanY}=state.site;
  cx = Math.min(left+spanX-1, Math.max(left,cx));
  cy = Math.min(top+spanY-1, Math.max(top,cy));
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
  const deposit=geology.at(index,surfaceSettings,mapSite ?? state.site);
  const allowed=state.site.deposits;
  return allowed===null || allowed?.includes(deposit?.id) ? deposit : null;
}
function footprint(x,y,w,h,color,stroke,target=ctx) {
  Isometric.footprint(target,screen,x,y,w,h,color,stroke);
}
function blocked(x,y,recipe) {
  const {width:w,height:h}=recipe.footprint;
  if(!inside([x+w-1,y+h-1]) || (recipe.deposit && deposit(y*size+x)?.id!==recipe.deposit)) return true;
  let low=15, high=0;
  for(let dy=0;dy<h;dy++) for(let dx=0;dx<w;dx++) {
    const cell=mapOffset(x+dx,y+dy);
    if(owners[cell]) return true;
    if(climateData) {
      const elevation=climateData.getUint8(cell*5), water=climateData.getUint8(cell*5+4);
      if(elevation===0 || elevation===15 || (water===2 && elevation<6)) return true;
      low=Math.min(low,elevation);
      high=Math.max(high,elevation);
    }
  }
  return high-low>2;
}
function climateColor(cell,x,y) {
  const at=cell*5;
  const elevation=climateData.getUint8(at), water=climateData.getUint8(at+4);
  const sun=climateData.getUint8(at+1)/100;
  const temperature=climateData.getInt16(at+2,true)/10;
  const warmth=Math.max(-1,Math.min(1,temperature/70));
  if(climateOverlay==="elevation") return "hsla(32,18%,"+(25+elevation*3)+"%,.65)";
  if(climateOverlay==="sunlight") return "hsla(41,28%,"+(27+sun*46)+"%,.66)";
  if(climateOverlay==="temperature") return warmth<0
    ? "hsla(209,18%,56%,"+(.25-warmth*.43)+")"
    : "hsla(25,44%,59%,"+(.25+warmth*.43)+")";
  const east=x+1<mapSite.left+mapSite.width ? climateData.getUint8((cell+1)*5) : elevation;
  const south=y+1<mapSite.top+mapSite.height ? climateData.getUint8((cell+mapSite.width)*5) : elevation;
  const relief=elevation*.7+(elevation-east)*2+(south-elevation);
  const light=30+sun*17+relief*.65;
  if(elevation===0) return "hsl(203,23%,"+(31+sun*7)+"%)";
  if(elevation===15) return "hsla(32,12%,"+(light+9)+"%,.7)";
  if(water===2) return "hsl("+(206-elevation*3)+",30%,"+(31+sun*6+elevation*3.3)+"%)";
  if(water===1) return "hsl(202,17%,"+(57+sun*12+elevation*.5)+"%)";
  const hue=warmth<0 ? 38-warmth*13 : 34-warmth*10;
  const saturation=warmth<0 ? 23+warmth*10 : 25+warmth*12;
  return "hsla("+hue+","+saturation+"%,"+light+"%,"+(water===3 ? .75 : .58)+")";
}
function terrainShade(x,y) {
  if(!mapSite?.generated) return Isometric.shade(x,y);
  const seed=surfaceSettings.seed;
  return Isometric.shade(x+(seed & 65535),y+(seed>>>16));
}
const terrainChunks=new Map();
const chunkSize=12, terrainBudget=48*1024*1024;
let terrainBytes=0;

function resetTerrainCache() {
  terrainChunks.clear();
  terrainBytes=0;
}

function terrainChunk(left,top,right,bottom,raster) {
  const key=left+","+top+","+raster;
  let chunk=terrainChunks.get(key);
  if(chunk) {
    terrainChunks.delete(key);
    terrainChunks.set(key,chunk);
    return chunk;
  }
  const cols=right-left, rows=bottom-top;
  const image=document.createElement("canvas");
  image.width=Math.ceil(((cols+rows)*48+2)*raster);
  image.height=Math.ceil(((cols+rows)*24+2)*raster);
  const g=image.getContext("2d");
  const offset=rows*48+1;
  g.setTransform(raster,0,0,raster,0,0);
  for(let diagonal=0;diagonal<cols+rows-1;diagonal++) {
    for(let x=Math.max(0,diagonal-rows+1);x<=Math.min(cols-1,diagonal);x++) {
      const y=diagonal-x, px=offset+(x-y)*48, py=diagonal*24;
      const shade=terrainShade(left+x,top+y);
      const orbit=mapSite.kind==="orbit";
      const ore=orbit ? null : deposit((top+y)*size+left+x);
      Isometric.drawGround(g,px,py,shade,orbit,tileSprites,ore?.index);
      if(!orbit && climateData) {
        const color=climateColor(mapOffset(left+x,top+y),left+x,top+y);
        if(color) Sprites.polygon(g,[[px,py],[px+48,py+24],[px,py+48],[px-48,py+24]],color);
      }
    }
  }
  const bytes=image.width*image.height*4;
  while(terrainBytes+bytes>terrainBudget && terrainChunks.size) {
    const oldest=terrainChunks.keys().next().value;
    terrainBytes-=terrainChunks.get(oldest).bytes;
    terrainChunks.delete(oldest);
  }
  chunk={image,bytes};
  terrainChunks.set(key,chunk);
  terrainBytes+=bytes;
  return chunk;
}

function drawTerrain(g,x0,y0,x1,y1) {
  const {left,top,width:cols,height:rows}=mapSite;
  const raster=2**Math.max(-2,Math.min(0,Math.round(Math.log2(scale*dpr))));
  const firstX=Math.floor((x0-left)/chunkSize), lastX=Math.floor((x1-left)/chunkSize);
  const firstY=Math.floor((y0-top)/chunkSize), lastY=Math.floor((y1-top)/chunkSize);
  for(let diagonal=firstX+firstY;diagonal<=lastX+lastY;diagonal++) {
    for(let x=Math.max(firstX,diagonal-lastY);x<=Math.min(lastX,diagonal-firstY);x++) {
      const a=left+x*chunkSize, b=top+(diagonal-x)*chunkSize;
      const right=Math.min(a+chunkSize,left+cols), bottom=Math.min(b+chunkSize,top+rows);
      const [px,py]=screen(a,b), drawX=px-((bottom-b)*48+1)*scale;
      if(drawX>=width || drawX+((right-a+bottom-b)*48+2)*scale<=0 ||
         py>=height || py+((right-a+bottom-b)*24+2)*scale<=0) continue;
      const {image}=terrainChunk(a,b,right,bottom,raster);
      g.drawImage(image,drawX,py,image.width/raster*scale,image.height/raster*scale);
    }
  }
}

const groundLayer=document.createElement("canvas");
const buildingLayer=document.createElement("canvas");
let sceneKey;
function rebuildScene(water) {
  for(const layer of [groundLayer,buildingLayer]) {
    if(layer.width!==canvas.width) layer.width=canvas.width;
    if(layer.height!==canvas.height) layer.height=canvas.height;
  }
  const ground=groundLayer.getContext("2d",{alpha:false}), structures=buildingLayer.getContext("2d");
  for(const g of [ground,structures]) g.setTransform(dpr,0,0,dpr,0,0);
  ground.fillStyle=mapSite.kind==="orbit"?"#101420":"#38251d";
  ground.fillRect(0,0,width,height);
  structures.clearRect(0,0,width,height);

  const pad=225*scale;
  const corners=[world(-pad,-pad),world(width+pad,-pad),world(-pad,height+pad),world(width+pad,height+pad)];
  const {left,top,width:spanX,height:spanY}=mapSite;
  const x0=Math.max(left,Math.floor(Math.min(...corners.map(p=>p[0]))));
  const x1=Math.min(left+spanX-1,Math.ceil(Math.max(...corners.map(p=>p[0]))));
  const y0=Math.max(top,Math.floor(Math.min(...corners.map(p=>p[1]))));
  const y1=Math.min(top+spanY-1,Math.ceil(Math.max(...corners.map(p=>p[1]))));
  drawTerrain(ground,x0,y0,x1,y1);
  const buildings=[];
  const wetColor=!climateData && water>=10?'rgba(47,119,135,'+Math.min(.75,.22+water/160)+')':null;
  for(let diagonal=x0+y0;diagonal<=x1+y1;diagonal++) {
    for(let x=Math.max(x0,diagonal-y1);x<=Math.min(x1,diagonal-y0);x++) {
      const y=diagonal-x, cell=mapOffset(x,y), type=grid[cell];
      if(wetColor && !owners[cell]) {
        const [px,py]=screen(x,y), a=48*scale,b=24*scale;
        if(px>=-50*scale && px<=width+50*scale && py<height && py+50*scale>=0 &&
           terrainShade(x,y)<2 && !deposit(y*size+x))
          Sprites.polygon(ground,[[px,py],[px+a,py+b],[px,py+b*2],[px-a,py+b]],wetColor,"#72a6a659");
      }
      if(type) {
        const w=widths[cell], h=heights[cell];
        const building=Isometric.place(screen,scale,type,x,y,w,h);
        const [sx,sy,,,,s]=building;
        if(sx+48*s>=0 && sx-48*s<width && sy+50*s>=0 && sy-50*s<height)
          buildings.push(building);
      }
    }
  }
  buildings.sort(Isometric.depth);
  drawnBuildings=buildings;
  visible=buildings.length;
  for(const building of buildings) Isometric.drawFoundation(ground,screen,mapSite.kind,building);
  for(const building of buildings) drawBuilding(structures,building);
}

function drawBuilding(target,building) {
  Isometric.drawBuilding(target,buildingSprites[building[2]-1],building);
}

function drawHighlight(highlight) {
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
    const owner=owners[mapOffset(x,y)]-1;
    if(recipe) footprint(x,y,recipe.footprint.width,recipe.footprint.height,invalid?"#fb77554c":"#ffdfa941",invalid?"#ff9c81":"#ffd09e");
    else if(owner<0) footprint(x,y,1,1,"#ffdfa941","#ffd09e");
    else {
      const cell=offsetOfIndex(owner);
      footprint(owner%size,Math.floor(owner/size),widths[cell],heights[cell],"#ffdfa941","#ffd09e");
    }
  }
}

function draw() {
  if(!canvas.width || !canvas.height) return;
  ctx.setTransform(dpr,0,0,dpr,0,0);
  if(!grid || mapWorldRevision!==state?.worldRevision) {
    ctx.fillStyle="#38251d";
    ctx.fillRect(0,0,width,height);
    drawnBuildings=[];
    visible=0;
    return;
  }
  const water=state.planet["surface-water"] || 0;
  const key=[mapWorldRevision,mapRevision,cx,cy,scale,width,height,dpr,water,climateOverlay].join(",");
  if(key!==sceneKey) {
    rebuildScene(water);
    sceneKey=key;
  }

  const highlight=cursor || selected;
  ctx.drawImage(groundLayer,0,0,width,height);
  drawHighlight(highlight);
  ctx.drawImage(buildingLayer,0,0,width,height);
  const owner=!anchor && mode<=0 && highlight && inside(highlight) ? owners[mapOffset(...highlight)]-1 : -1;
  const active=owner>=0 && drawnBuildings.find(building=>building[4]*size+building[3]===owner);
  if(active) {
    // Replay just the outline's clip to preserve which sprites cover it.
    const [, , ,x,y, ,w,h]=active;
    const corners=[screen(x,y),screen(x+w,y),screen(x+w,y+h),screen(x,y+h)];
    const x0=corners[3][0]-2, x1=corners[1][0]+2;
    const y0=corners[0][1]-2, y1=corners[2][1]+2;
    ctx.save();
    ctx.beginPath();
    ctx.rect(x0,y0,x1-x0,y1-y0);
    ctx.clip();
    ctx.drawImage(groundLayer,0,0,width,height);
    drawHighlight(highlight);
    for(const building of drawnBuildings) {
      const [px,py, , , ,s]=building;
      if(px+48*s+2<x0 || px-48*s-2>x1 || py+50*s+2<y0 || py-50*s-2>y1) continue;
      drawBuilding(ctx,building);
      if(building===active) footprint(x,y,w,h,"#ffffff00","#ffd09e");
    }
    ctx.restore();
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
    const measure=state?.developerMode;
    const start=measure?performance.now():0;
    draw();
    if(state?.climate) showClimateTile(cursor || selected);
    placeInfo();
    if(measure) {
      record(drawTimes,performance.now()-start);
      if(time>=nextDrawReport) {
        setText($("draw"),p95(drawTimes));
        setText($("count"),number.format(state.total)+" / "+number.format(visible));
        nextDrawReport=time+250;
      }
    }
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
function materialAvailable(material) {
  return !material.galaxy || Boolean(state?.visitedGalaxies.includes(material.galaxy));
}

function availableMaterials() {
  return catalog.materials.filter(materialAvailable);
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
    recipe.colonistsPerHour && "+"+recipe.colonistsPerHour+" colonist / hour",
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
    button.hidden=Boolean(recipe.galaxy);
    button.innerHTML='<img alt=""><span><strong></strong><small></small></span><span class="cost"></span>';
    button.children[0].src=buildingSprites[i].toDataURL();
    button.querySelector("strong").textContent=recipe.name;
    button.querySelector("small").textContent=buildingDetail(recipe);
    button.lastElementChild.textContent=formatAmounts(recipe.cost) || "Free";
    const tech=catalog.research.find(t=>t.id===recipe.research);
    const site=catalog.world.deposits.find(d=>d.id===recipe.deposit);
    button.title=[recipe.detail || buildingDetail(recipe),site?.name,tech && "Research: "+tech.name].filter(Boolean).join(" · ");
    button.onclick=()=>{chooseMode(i+1);canvas.focus({preventScroll:true});};
    $("builds").append(button);
  });
  $("category").onchange=()=>{
    const category=$("category").value;
    Array.from($("builds").children).forEach((button,i)=>{
      const building=catalog.buildings[i];
      button.hidden=Boolean(category && category!==building.category || building.galaxy && building.galaxy!==state?.site.galaxy ||
        building.research && building.research===state?.site.restrictedResearch);
    });
    if(mode>0 && category && catalog.buildings[mode-1].category!==category) chooseMode(0);
  };
  chooseMode(0);
}
function setText(element,value) {
  if(element.textContent!==value) element.textContent=value;
}
function setAttribute(element,name,value) {
  if(element.getAttribute(name)!==value) element.setAttribute(name,value);
}
function setProperty(element,name,value) {
  if(element[name]!==value) element[name]=value;
}

function display(next) {
  const redraw=!state || state.storm!==next.storm || state.planet["surface-water"]!==next.planet["surface-water"];
  state=next;
  setProperty($("climateControls"),"hidden",!next.climate);
  setProperty($("climateReadout"),"hidden",!next.climate);
  if(next.climate) {
    const {baseTemperature,humidity,effectiveBase}=next.climate;
    const key=[next.site.id,baseTemperature,humidity].join(",");
    if(climateFormKey!==key) {
      $("baseTemperature").value=baseTemperature;
      $("humidity").value=humidity;
      climateFormKey=key;
    }
    setText($("climateBase"),"Effective base: "+(effectiveBase/10).toFixed(1)+" °C. Changing climate may flood structures.");
  } else climateFormKey="";
  document.body.classList.toggle("playing",next.started);
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
    const material=catalog.materials[i];
    setProperty(stats[i][0].parentElement,"hidden",Boolean(material && !materialAvailable(material)));
    setText(stats[i][1],value);
    setText(stats[i][2],detail);
    setAttribute(stats[i][0],"aria-label",stats[i][3]+" "+value);
    setProperty(stats[i][2],"className",delta>0?"positive":delta<0?"negative":"");
  });
  const materials=availableMaterials();
  if(next.developerMode) syncTesterMaterials(materials);
  syncLogisticsMaterials(materials);
  catalog.world.metrics.forEach((metric,i)=>{
    const value=next.planet[metric.id];
    setText(metrics[i][1],(value/10).toFixed(1)+metric.unit+" / "+metric.target/10+metric.unit);
    setProperty(metrics[i][2],"value",value-metric.initial);
  });
  setProperty($("engineering"),"hidden",!next.site.planetaryEngineering);
  const unlocked=new Set(next.unlocked);
  const available=new Set(next.allowedBuildings);
  const researchMaterial=catalog.world.researchMaterial;
  setText($('researchProgress'),number.format(next.stock[researchMaterial])+" RP in stock / "+
    number.format(next.delta[researchMaterial])+" RP net per tick");
  catalog.research.forEach((t,i)=>{
    const [card,detail,button]=techCards[i];
    setProperty(card,"hidden",Boolean(t.galaxy && t.galaxy!==next.site.galaxy || t.id===next.site.restrictedResearch));
    const active=unlocked.has(t.id);
    const missing=t.requires.filter(id=>!unlocked.has(id));
    const points=next.stock[researchMaterial];
    const requirements=missing.map(id=>catalog.research.find(other=>other.id===id).name);
    card.classList.toggle("positive",active);
    setProperty(button,"hidden",active);
    setProperty(button,"disabled",points<t.points || missing.length>0);
    setText(detail,active?"Active":points>=t.points && !missing.length?"Ready to activate":
      number.format(points)+" / "+number.format(t.points)+" RP"+
      (requirements.length?" · Activate first: "+requirements.join(", "):""));
  });
  Array.from($("builds").children).forEach((button,i)=>{
    const building=catalog.buildings[i], id=building.research;
    const category=$("category").value;
    setProperty(button,"hidden",Boolean(category && category!==building.category || building.galaxy && building.galaxy!==next.site.galaxy ||
      building.research && building.research===next.site.restrictedResearch));
    const locked=Boolean(id && !unlocked.has(id));
    setProperty(button,"disabled",!available.has(i+1));
    button.classList.toggle("locked",locked);
    setText(button.querySelector("strong"),(locked?"🔒 ":"")+catalog.buildings[i].name);
  });
  setText($("pause"),next.paused?"Resume":"Pause");
  setText($("speed"),next.speed+"× speed");
  setProperty($("save"),"disabled",next.stress);
  setProperty($("saveAs"),"disabled",next.stress);
  setProperty($("weather"),"hidden",!next.storm);
  setText($("world"),next.stress?"Benchmark sector":next.site.name);
  setText($("clock"),"Sol "+(Math.floor(next.tick/24)+1)+" · "+String(next.tick%24).padStart(2,"0")+":00");
  setText($("mission"),next.complete?"Terraforming targets met":next.shortage?"Life support shortage":
    next.unlocked.length===0?"Build a lab to unlock new facilities":"Research and terraforming underway");
  setText($("tick"),next.tickMs.toFixed(3)+" ms");
  setProperty($("testerTools"),"hidden",!next.developerMode);
  setProperty($("openLogistics"),"disabled",next.stress);
  setText($("stress"),next.stress?"Return to colony":"Load 100k stress map");
  setProperty($("sweep"),"hidden",!next.stress);
  if(redraw) requestDraw();
}
function mapOffset(x,y) {
  return (y-mapSite.top)*mapSite.width+x-mapSite.left;
}
const offsetOfIndex=index=>mapOffset(index%size,Math.floor(index/size));

function setOwner(cell,w,h,owner) {
  for(let y=0;y<h;y++) owners.fill(owner,cell+y*mapSite.width,cell+y*mapSite.width+w);
}
function applyTileEdit(edit) {
  const cell=offsetOfIndex(edit.index);
  if(grid[cell]) setOwner(cell,widths[cell],heights[cell],0);
  grid[cell]=edit.type;
  widths[cell]=edit.type?edit.width:0;
  heights[cell]=edit.type?edit.height:0;
  if(edit.type) setOwner(cell,edit.width,edit.height,edit.index+1);
}
async function syncMap() {
  const site=state.site, revision=state.worldRevision;
  const requests=[fetch("/api/map",{cache:"no-store"}),useEnvironmentSprites(site)];
  if(surfaceRevision!==revision) requests.push(fetch("/api/surface",{cache:"no-store"}));
  if(surfaceRevision!==revision && state.climate) requests.push(fetch("/api/climate",{cache:"no-store"}));
  const [response,,terrain,climate]=await Promise.all(requests);
  if(!response.ok) throw new Error("Could not load the sector map.");
  if(Number(response.headers.get("X-World-Revision"))!==revision) throw new Error("Base changed during map load.");
  const cells=new Uint8Array(await response.arrayBuffer()), area=site.width*site.height;
  if(cells.length!==area*3) throw new Error("Incomplete sector map.");
  if(terrain) {
    if(!terrain.ok) throw new Error("Could not load geology.");
    if(Number(terrain.headers.get("X-World-Revision"))!==revision) throw new Error("Base changed during terrain load.");
    let climateView;
    if(climate) {
      if(!climate.ok || Number(climate.headers.get("X-World-Revision"))!==revision)
        throw new Error("Base changed during climate load.");
      const data=await climate.arrayBuffer();
      if(data.byteLength!==area*5) throw new Error("Incomplete climate map.");
      climateView=new DataView(data);
    }
    surfaceSettings=await terrain.json();
    climateData=climateView;
    surfaceRevision=revision;
    resetTerrainCache();
  }
  mapSite=site;
  grid=cells.subarray(0,area);
  widths=cells.subarray(area,2*area);
  heights=cells.subarray(2*area);
  owners=new Uint32Array(area);
  for(let cell=0;cell<area;cell++) if(grid[cell]) {
    const x=site.left+cell%site.width, y=site.top+Math.floor(cell/site.width);
    setOwner(cell,widths[cell],heights[cell],y*size+x+1);
  }
  mapRevision=Number(response.headers.get("X-Map-Revision"));
  mapWorldRevision=revision;
  sceneKey=undefined;
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
    if(response.ok && grid && mapWorldRevision===state.worldRevision && result.edits && result.state.revision===previous+1) {
      for(const edit of result.edits) applyTileEdit(edit);
      mapRevision=result.state.revision;
      requestDraw();
    }
    if(state.started && (state.revision!==mapRevision || surfaceRevision!==state.worldRevision)) await syncMap();
    if(["stress","new","load","import","menu","select-base"].includes(action) && response.ok) {
      sweepStart=undefined;
      $("sweep").disabled=false;
      frameTimes.length=drawTimes.length=0;
      $("frames").textContent="—";
      selected=cursor=anchor=undefined;
      $("explorationDialog").close();
      $("buildingInfo").hidden=true;
      $("inspect").textContent="Select a tile to inspect it.";
      chooseMode(0);
      center();
    }
    if(response.ok && ["menu","save","import"].includes(action)) await refreshSaveMenu();
    if(response.ok && action==="new") randomizeCampaignSeed();
    await refreshLogistics();
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
    await refreshLogistics();
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
const inside=([x,y])=> {
  const {left,top,width,height}=state.site;
  return x>=left && y>=top && x<left+width && y<top+height;
};
function inspectTile([x,y]) {
  if(!grid || mapWorldRevision!==state.worldRevision || !inside([x,y])) return;
  const index=y*size+x, owner=owners[mapOffset(x,y)]-1, resource=deposit(index);
  const recipe=owner>=0?catalog.buildings[grid[offsetOfIndex(owner)]-1]:null;
  $("inspect").textContent=x+", "+y+"  ·  "+(recipe?.name || resource?.name ||
    (state.site.kind==="orbit" ? "Open station tile" : "Open regolith"));
}
function showClimateTile(tile) {
  if(!climateData || !tile || mapWorldRevision!==state?.worldRevision || !inside(tile)) {
    setText($("climateReadout"),"Point at a tile to inspect its climate.");
    return;
  }
  const [x,y]=tile, at=mapOffset(x,y)*5;
  const elevation=climateData.getUint8(at), sun=climateData.getUint8(at+1);
  const temperature=climateData.getInt16(at+2,true)/10, water=climateData.getUint8(at+4);
  const parts=[x+", "+y,"Elevation "+elevation+(elevation===6?" (shore)":""),waterNames[water],
    sun+"% sunlight",temperature.toFixed(1)+" °C"];
  const recipe=catalog.buildings[mode-1];
  if(recipe && !anchor && !blocked(x,y,recipe)) {
    if(recipe.art==="solar") parts.push("Expected solar output: "+Math.round(recipe.generation*sun/100));
    if(recipe.minimumTemperature!==null || recipe.maximumTemperature!==null) {
      const min=recipe.minimumTemperature ?? "unbounded", max=recipe.maximumTemperature ?? "unbounded";
      parts.push("Operating range: "+min+" to "+max+" °C");
    }
    let floodRisk=false, tooHotOrCold=false;
    for(let dy=0;dy<recipe.footprint.height;dy++) for(let dx=0;dx<recipe.footprint.width;dx++) {
      const footprintCell=mapOffset(x+dx,y+dy)*5;
      const phase=climateData.getUint8(footprintCell+4), degrees=climateData.getInt16(footprintCell+2,true)/10;
      floodRisk ||= phase===1 || phase===3;
      tooHotOrCold ||= degrees<(recipe.minimumTemperature ?? -500) || degrees>(recipe.maximumTemperature ?? 500);
    }
    if(tooHotOrCold) parts.push("Outside operating temperature");
    if(floodRisk) parts.push("Flood risk if ice melts or the seabed refills");
  }
  setText($("climateReadout"),parts.join(" · "));
}
function placeInfo() {
  const box=$("buildingInfo");
  if(box.hidden || !state?.inspected) return;
  const index=state.inspected.index;
  const cell=offsetOfIndex(index);
  const [px,py]=screen(index%size+(widths[cell]-1)/2,Math.floor(index/size)+(heights[cell]-1)/2);
  const left=Math.min(width-box.offsetWidth-6,px+28);
  box.style.left=Math.max(6,left)+"px";
  box.style.top=Math.max(6,Math.min(height-box.offsetHeight-6,py-box.offsetHeight*.65))+"px";
}
function updateInfo(info) {
  const box=$("buildingInfo");
  const cell=selected && inside(selected) ? mapOffset(...selected) : -1;
  if(!info || !state.started || mode!==0 || owners?.[cell]!==info.index+1) {
    box.hidden=true;
    $("explorationDialog").close();
    return;
  }
  const recipe=catalog.buildings[info.type-1];
  $("infoDescription").textContent=recipe.description;
  $("openExploration").hidden=!state.telescope;
  if(state.telescope) updateExploration(state.telescope,info.index);
  const assembly=$("shipAssembly");
  assembly.hidden=recipe.spaceRole!=="assembly" || state.stress;
  if(!assembly.hidden) {
    const select=$("shipType");
    const render=()=>{
      const type=catalog.shipTypes.find(item=>item.id===(info.assembly?.type ?? select.value));
      select.disabled=Boolean(info.assembly);
      const locked=type?.research && !state.unlocked.includes(type.research);
      $("beginAssembly").disabled=Boolean(info.assembly) || !info.assemblyReady || !type || Boolean(locked);
      $("assemblyStatus").textContent=info.assembly
        ? type.name+" · "+info.assembly.ticksLeft+" ticks remaining"+(info.assemblyReady?"":" · Paused")
        : type ? formatAmounts(type.cost)+" · "+type.buildTicks+" ticks · "+type.payload+" cargo · "+
          number.format(type.range)+" UoD range · "+number.format(type.speed)+" UoD/sol"+
          (locked?" · Requires "+catalog.research.find(tech=>tech.id===type.research).name:"")+
          (info.assemblyReady?"":" · Requires an operating yard and cargo facility") : "";
    };
    select.onchange=render;
    assembly.onsubmit=event=>{
      event.preventDefault();
      command("assemble",{index:info.index,shipType:select.value});
    };
    render();
  }
  $("infoName").textContent=recipe.name+(recipe.upgrades.length?" · Level "+info.level:"");
  const status=info.disabled ? "Shut down" : info.running ? "Operating" :
    info.temperatureBlocked ? "Temperature out of range" :
    recipe.beds && info.ratedPowerUse ? "Waiting for power" : "Idle";
  const lines=[status+" / "+info.width+"x"+info.height+" tiles"];
  if(recipe.beds) lines.push("Housing capacity "+(info.running ? recipe.beds : 0)+" / "+recipe.beds+" beds");
  if(info.crew) lines.push("Workers "+info.workers+" / "+info.crew);
  if(info.ratedPowerOutput) lines.push("Power output "+info.powerOutput+" / "+info.ratedPowerOutput);
  if(info.ratedPowerUse) lines.push("Power draw "+info.powerUse+" / "+info.ratedPowerUse);
  if(info.temperatureBlocked) lines.push("Offline: footprint is outside the operating temperature range.");
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
    const benefit=recipe.spaceRole==="telescope" ? "30× observation range" : info.upgrade.multiplier+"× base yield";
    $("upgradeBuilding").title=benefit+" · +"+info.upgrade.extraPower+" power · +"+info.upgrade.extraCrew+" crew";
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
$("climateOverlay").onchange=()=>{
  climateOverlay=$("climateOverlay").value;
  resetTerrainCache();
  requestDraw();
};
$("climateForm").onsubmit=event=>{
  event.preventDefault();
  command("climate",{climate:{baseTemperature:Number($("baseTemperature").value),humidity:Number($("humidity").value)}});
};
$("in").onclick=()=>zoom(1.2);
$("out").onclick=()=>zoom(1/1.2);
$("inspectMode").onclick=()=>chooseMode(0);
$("salvage").onclick=()=>chooseMode(-1);
$("area").onclick=()=>setArea(!areaMode);
for(const action of ["save","pause","speed","stress"]) $(action).onclick=()=>command(action);
const controlTabs=[...document.querySelectorAll("[data-control]")];
let activePanel, controlTab="researchPanel";
function openPanel(id, tab=controlTab) {
  activePanel=id;
  $("constructionPanel").hidden=id!=="constructionPanel";
  $("controlPanel").hidden=id!=="controlPanel";
  controlTab=tab;
  for(const button of controlTabs) {
    const active=button.dataset.control===tab;
    $(button.dataset.control).hidden=!active;
    setAttribute(button,"aria-pressed",String(active));
  }
  setAttribute($("openConstruction"),"aria-expanded",String(id==="constructionPanel"));
  setAttribute($("openControl"),"aria-expanded",String(id==="controlPanel"));
  setAttribute($("openLogistics"),"aria-expanded",String(id==="controlPanel" && tab==="logistics"));
  refreshLogistics().catch(error=>notice(error.message,true));
}
$("openControl").onclick=()=>openPanel(activePanel==="controlPanel" ? undefined : "controlPanel");
$("openConstruction").onclick=()=>openPanel(activePanel==="constructionPanel" ? undefined : "constructionPanel");
$("openLogistics").onclick=()=>openPanel("controlPanel","logistics");
$("closeConstruction").onclick=()=>{openPanel();$("openConstruction").focus();};
$("closeControl").onclick=()=>{openPanel();$("openControl").focus();};
for(const button of controlTabs) button.onclick=()=>openPanel("controlPanel",button.dataset.control);
function syncTesterMaterials(materials) {
  const list=$("testerMaterials"), current=[...list.querySelectorAll("input")];
  if(current.length===materials.length && current.every((input,i)=>input.value===materials[i].id)) return;
  const previous=new Map(current.map(input=>[input.value,input.checked]));
  list.replaceChildren(...materials.map(material=>{
    const label=document.createElement("label");
    const input=Object.assign(document.createElement("input"),{
      type:"checkbox",value:material.id,checked:previous.get(material.id) ?? true
    });
    label.append(input,material.name);
    return label;
  }));
  updateTesterGrant();
}
function updateTesterGrant() {
  $("testerGrantButton").disabled=!$("testerMaterials").querySelector("input:checked");
}
function selectTesterMaterials(checked) {
  for(const input of $("testerMaterials").querySelectorAll("input")) input.checked=checked;
  updateTesterGrant();
}
$("testerMaterials").onchange=updateTesterGrant;
$("testerSelectAll").onclick=()=>selectTesterMaterials(true);
$("testerClear").onclick=()=>selectTesterMaterials(false);
$("testerUnlockResearch").onclick=()=>command("tester-unlock-research");
$("testerGrant").onsubmit=event=>{
  event.preventDefault();
  const materials=[...$("testerMaterials").querySelectorAll("input:checked")].map(input=>input.value);
  if(materials.length) command("tester-grant",{materials,amount:Number($("testerAmount").value)});
};
$("testerStep").onsubmit=event=>{
  event.preventDefault();
  command("tester-step",{amount:Number($("testerTicks").value)});
};
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
async function refreshSaveMenu() {
  const response=await fetch("/api/saves",{cache:"no-store"});
  if(!response.ok) throw new Error("Could not read colony saves.");
  const saves=await response.json();
  setOptions($("saveSelect"),saves.map(save=>[
    save.id,save.name+" ("+new Date(save.updated).toLocaleString()+")"
  ]));
  $("saveSelect").disabled=$("loadSaved").disabled=saves.length===0;
}
$("loadSaved").onclick=()=>command("load",{saveSlot:$("saveSelect").value});
$("saveAs").onclick=()=>{
  const name=prompt("New save name");
  if(name?.trim()) command("save",{saveSlot:name.trim()});
};
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

async function useEnvironmentSprites(site) {
  const env=Artwork.environment(site.kind), key=site.galaxy+"/"+env;
  if(key===spriteEnvironment) return;
  const resources=[...catalog.materials,...statusResources];
  if(!environmentSprites.has(key)) environmentSprites.set(key,(async()=>{
    const [sprites,icons]=await Promise.all([
      Promise.all(catalog.buildings.map(building=>Isometric.loadBuilding(building,site.kind,site.galaxy))),
      Promise.all(resources.map(resource=>Artwork.sprite(site.galaxy,env,"resource",resource.id)))
    ]);
    return {sprites,masks:spriteMasks(sprites),icons};
  })());
  const {sprites,masks:nextMasks,icons}=await environmentSprites.get(key);
  if(state?.site.id!==site.id) return;
  buildingSprites=sprites;
  masks=nextMasks;
  spriteEnvironment=key;
  Array.from($("builds").children).forEach((button,i)=>button.firstElementChild.src=sprites[i].toDataURL());
  icons.forEach((icon,i)=>{
    if(!icon) {stats[i][4].textContent=resources[i].symbol;return;}
    const img=document.createElement("img");
    img.alt="";
    img.src=icon instanceof HTMLCanvasElement ? icon.toDataURL() : icon.src;
    stats[i][4].replaceChildren(img);
  });
}

function spriteMasks(sprites) {
  const canvas=Object.assign(document.createElement("canvas"),{width:96,height:100});
  const g=canvas.getContext("2d",{willReadFrequently:true});
  return sprites.map(sprite=>{
    g.clearRect(0,0,96,100);
    g.drawImage(sprite,0,0,96,100);
    return g.getImageData(0,0,96,100).data;
  });
}
async function initialize() {
  try {
    const response=await fetch("/api/catalog",{cache:"no-store"});
    if(!response.ok) throw new Error("Catalog unavailable");
    catalog=await response.json();
    const icons={ore:"◆",water:"◈",food:"✿",alloy:"▰",research:"⚗",silicon:"◇",titanium:"⬡",brine:"≋",uranium:"☢",propellant:"↑"};
    stats=[...catalog.materials,...statusResources].map(resource=>makeStats(resource.name,icons[resource.id] ?? resource.symbol));
    catalog.materials.forEach((material,i)=>stats[i][0].parentElement.hidden=!materialAvailable(material));
    metrics=catalog.world.metrics.map(makeMetric);
    techCards=catalog.research.map(t=>{
      const card=document.createElement("div");
      card.textContent=t.name;
      card.hidden=Boolean(t.galaxy);
      const detail=document.createElement("small"), button=document.createElement("button");
      button.type="button";
      button.textContent="Activate";
      button.setAttribute("aria-label","Activate "+t.name);
      button.onclick=()=>command("activate-research",{technology:t.id});
      card.append(detail,button);
      $("research").append(card);
      return [card,detail,button];
    });
    for(const type of catalog.shipTypes) $("shipType").add(new Option(type.name,type.id));
    const deposits=catalog.world.deposits.map((d,index)=>({...d,index:index+1}));
    [buildingSprites,tileSprites]=await Promise.all([
      Promise.all(catalog.buildings.map(building=>Isometric.loadSprite(building))),
      Sprites.makeTiles(deposits)
    ]);
    masks=spriteMasks(buildingSprites);
    deposits.forEach(d=>{
      const key=document.createElement("span");
      key.style.color=d.colors[0];
      key.textContent="◆ "+d.name;
      $("deposits").append(key);
    });
    geology=makeGeology(catalog,deposits);
    randomizeCampaignSeed=createMapEditor(catalog,deposits,geology,map=>command("new",{map}));
    populateMenu();
    initializeLogistics();
    await refreshSaveMenu();
    poll();
  } catch(error) {
    notice("Could not load the game catalog. "+error.message,true);
  }
}
initialize();

