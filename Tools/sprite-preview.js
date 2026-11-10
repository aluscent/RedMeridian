"use strict";

const $=id=>document.getElementById(id);
const canvas=$("scene"),picks=[...document.querySelectorAll(".building")];
const kinds={mars:[4,27,24],orbit:[6,27,32]};
const palette=["#f6c379","#8ce7dd","#e1aeff"];
const spriteCache=new Map();
let catalog,tiles,scene,request=0;

function available(building,kind) {
  if(kind==="mars") return building.spaceRole!=="dock";
  return !building.deposit && !Object.keys(building.effects || {}).length && building.art!=="wind" &&
    (building.art!=="launch" || building.spaceRole==="assembly") &&
    building.spaceRole!=="terminal" && !building.forecastsWeather;
}

function fillChoices() {
  const kind=$("environment").value;
  const options=catalog.filter(building=>available(building,kind));
  $("axis").value=kind==="orbit" ? "y" : "x";
  picks.forEach((select,i)=>{
    select.replaceChildren(new Option("Empty",""),...options.map(building=>{
      const {width=1,height=1}=building.footprint || {};
      return new Option(building.name+" ("+width+" × "+height+")",building.id);
    }));
    select.value=String(kinds[kind][i]);
  });
}

async function modelFor(building,kind,x,y) {
  const {width:w=1,height:h=1}=building.footprint || {};
  const key=kind+":"+building.id;
  if(!spriteCache.has(key)) spriteCache.set(key,Isometric.loadBuilding({...building,footprint:{width:w,height:h}},kind,"milky-way"));
  return {building,x,y,w,h,sprite:await spriteCache.get(key)};
}

let review=[];
async function prepareCatalog(id,kind) {
  const options=catalog.filter(building=>available(building,kind));
  const models=await Promise.all(options.map(building=>modelFor(building,kind,1,1)));
  if(id!==request) return;
  review=models.map(model=>{
    const card=document.createElement("figure"), image=document.createElement("canvas"), label=document.createElement("figcaption");
    label.textContent=model.building.name+" ("+model.w+" x "+model.h+")";
    card.append(image,label);
    return {card,image,scene:{kind,models:[model],cols:model.w+2,rows:model.h+2}};
  });
  $("catalog").replaceChildren(...review.map(item=>item.card));
  $("status").textContent=models.length+" "+(kind==="orbit"?"orbital":"planetary")+" buildings";
  render();
}

async function prepare() {
  const id=++request,kind=$("environment").value,axis=$("axis").value,gap=Number($("gap").value);
  const audit=$("catalogMode").checked;
  $("catalog").hidden=!audit;
  canvas.hidden=audit;
  for(const control of [...picks,$("axis"),$("gap")]) control.disabled=audit;
  if(audit) return prepareCatalog(id,kind);
  const buildings=picks.map(select=>catalog.find(building=>building.id===Number(select.value))).filter(Boolean);
  let cursor=1, cols=3, rows=3;
  const models=await Promise.all(buildings.map(async building=>{
    const {width:w=1,height:h=1}=building.footprint || {};
    const x=axis==="x" ? cursor : 1, y=axis==="y" ? cursor : 1;
    cursor+=(axis==="x" ? w : h)+gap;
    cols=Math.max(cols,x+w+1);
    rows=Math.max(rows,y+h+1);
    return modelFor(building,kind,x,y);
  }));
  if(id!==request) return;
  scene={kind,models,cols,rows};
  $("status").textContent=models.map(({building,w,h})=>building.name+" "+w+" × "+h).join(" · ") || "Choose a building to preview.";
  render();
}

function render() {
  if($("catalogMode").checked) {
    for(const item of review) renderScene(item.image,item.scene);
  } else if(scene) renderScene(canvas,scene);
}

function renderScene(canvas,scene) {
  const g=canvas.getContext("2d");
  const {kind,models,cols,rows}=scene,orbit=kind==="orbit";
  const width=canvas.clientWidth,height=canvas.clientHeight,ratio=window.devicePixelRatio || 1;
  canvas.width=Math.round(width*ratio);
  canvas.height=Math.round(height*ratio);
  g.setTransform(ratio,0,0,ratio,0,0);
  g.fillStyle=orbit?"#101420":"#38251d";
  g.fillRect(0,0,width,height);
  const screen=(x,y)=>Isometric.project(x,y,1);
  const ordered=models.map(({building,x,y,w,h,sprite})=>({
    placement:Isometric.place(screen,1,building.id,x,y,w,h),sprite
  })).sort((a,b)=>Isometric.depth(a.placement,b.placement));
  const bounds={left:-rows*48,right:cols*48,top:0,bottom:(cols+rows)*24};
  for(const {placement:[x,y,,,,scale]} of ordered){
    bounds.left=Math.min(bounds.left,x-48*scale);
    bounds.right=Math.max(bounds.right,x+48*scale);
    bounds.top=Math.min(bounds.top,y-50*scale);
    bounds.bottom=Math.max(bounds.bottom,y+50*scale);
  }
  const spanX=bounds.right-bounds.left,spanY=bounds.bottom-bounds.top;
  const scale=Math.min((width-24)/spanX,(height-24)/spanY,1.65);
  if(scale<=0) return;
  g.translate((width-spanX*scale)/2-bounds.left*scale,(height-spanY*scale)/2-bounds.top*scale);
  g.scale(scale,scale);
  for(let diagonal=0;diagonal<cols+rows-1;diagonal++)
    for(let x=Math.max(0,diagonal-rows+1);x<=Math.min(cols-1,diagonal);x++){
      const y=diagonal-x,[px,py]=screen(x,y);
      Isometric.drawGround(g,px,py,Isometric.shade(x,y),orbit,tiles);
    }
  for(const {placement} of ordered) Isometric.drawFoundation(g,screen,kind,placement);
  for(const {placement,sprite} of ordered) Isometric.drawBuilding(g,sprite,placement);
  if($("outlines").checked){
    g.setLineDash([5,3]);
    models.forEach(({x,y,w,h},i)=>Isometric.footprint(g,screen,x,y,w,h,"#ffffff00",palette[i]));
    g.setLineDash([]);
  }
}

async function initialize() {
  const response=await fetch("/config");
  if(!response.ok) throw Error("Building catalog unavailable");
  catalog=await response.json();
  tiles=await Sprites.makeTiles([]);
  fillChoices();
  await prepare();
  $("environment").onchange=()=>{fillChoices();update();};
  $("catalogMode").onchange=update;
  for(const control of [...picks,$("axis"),$("gap")]) control.onchange=update;
  $("outlines").onchange=render;
  new ResizeObserver(render).observe(document.querySelector("main"));
}
function report(error){$("status").textContent=error.message;}
function update(){prepare().catch(report);}
initialize().catch(report);
