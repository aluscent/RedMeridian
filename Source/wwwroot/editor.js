"use strict";

// The editor and the play map share the same deterministic geology.
function makeGeology(catalog, deposits) {
  const buckets=Array(catalog.world.depositBuckets).fill(null);
  const fixed=new Map(catalog.world.depositOverrides.map(o=>[o.y*1024+o.x,o.deposit]));
  const byId=new Map(deposits.map(d=>[d.id,d]));
  for(const deposit of deposits) for(const bucket of deposit.buckets) buckets[bucket]=deposit;
  function at(index,map) {
    if(fixed.has(index)) return byId.get(fixed.get(index));
    if(Object.hasOwn(map.paint || {},index)) return byId.get(map.paint[index]);
    let hash=Math.imul(index,0x9e3779b9)^map.seed;
    hash=Math.imul(hash^(hash>>>16),0x7feb352d);
    hash=(hash^(hash>>>15))>>>0;
    const deposit=buckets[hash%buckets.length];
    const quotient=Math.floor(hash/buckets.length);
    const variation=quotient%100;
    if(deposit) return variation<map.abundance?deposit:null;
    return variation<map.abundance-100?deposits[Math.floor(quotient/100)%deposits.length]:null;
  }
  return {at,fixed};
}

function createMapEditor(catalog, deposits, geology, start) {
  const el=id=>document.getElementById(id);
  const preview=el("preview"), ctx=preview.getContext("2d");
  const cols=32, rows=24, tile=18;
  const starter=new Set();
  for(const {x,y,building} of catalog.world.starter) {
    const {width,height}=catalog.buildings[building-1].footprint;
    for(let dy=0;dy<height;dy++) for(let dx=0;dx<width;dx++) starter.add((y+dy)*1024+x+dx);
  }
  let originX=496,originY=500, paint={}, editing=false, pointer;
  const abundance=el("abundance");
  const seed=el("seed");
  const brush=el("brush");
  for(const d of deposits) brush.add(new Option(d.name,d.id));
  const model=()=>({seed:Number(seed.value)||0,abundance:Number(abundance.value),paint:{...paint}});
  function draw() {
    const map=model();
    el("abundanceValue").textContent=abundance.value+"%";
    el("previewCoords").textContent=originX+", "+originY+" · "+Object.keys(paint).length+" painted tiles";
    for(let y=0;y<rows;y++) for(let x=0;x<cols;x++) {
      const index=(originY+y)*1024+originX+x;
      const d=geology.at(index,map);
      ctx.fillStyle=d?.colors[1] || ((x+y)%2?"#5d392b":"#663d2f");
      ctx.fillRect(x*tile,y*tile,tile-1,tile-1);
      if(starter.has(index)) {
        ctx.strokeStyle="#fbe5bb";
        ctx.strokeRect(x*tile+2,y*tile+2,tile-5,tile-5);
      }
      if(Object.hasOwn(paint,index)) {
        ctx.fillStyle="#fff1d5";
        ctx.fillRect(x*tile+3,y*tile+3,3,3);
      }
    }
  }
  function paintAt(event) {
    const rect=preview.getBoundingClientRect();
    const x=Math.floor((event.clientX-rect.left)/rect.width*cols);
    const y=Math.floor((event.clientY-rect.top)/rect.height*rows);
    if(x<0 || y<0 || x>=cols || y>=rows) return;
    const index=(originY+y)*1024+originX+x;
    if(geology.fixed.has(index)) {
      el("menuNotice").textContent="The starter iron and ice deposits are protected.";
      return;
    }
    if(brush.value==="natural") delete paint[index];
    else {
      if(!Object.hasOwn(paint,index) && Object.keys(paint).length>=16384) {
        el("menuNotice").textContent="This map has reached the 16,384-tile edit limit.";
        return;
      }
      paint[index]=brush.value==="empty"?null:brush.value;
    }
    el("menuNotice").textContent="";
    draw();
  }
  preview.addEventListener("pointerdown",event=>{
    if(!editing || event.button!==0) return;
    pointer=event.pointerId;
    preview.setPointerCapture(pointer);
    paintAt(event);
  });
  preview.addEventListener("pointermove",event=>{if(event.pointerId===pointer) paintAt(event);});
  preview.addEventListener("lostpointercapture",()=>{pointer=undefined;});
  el("editorMode").onclick=()=>{
    editing=!editing;
    el("editorTools").hidden=!editing;
    el("editorMode").setAttribute("aria-pressed",editing);
    preview.classList.toggle("painting",editing);
  };
  el("newSeed").onclick=()=>{
    seed.value=crypto.getRandomValues(new Int32Array(1))[0];
    draw();
  };
  seed.oninput=draw;
  abundance.oninput=draw;
  document.querySelectorAll("[data-pan]").forEach(button=>{
    button.onclick=()=>{
      const [dx,dy]=button.dataset.pan.split(",").map(Number);
      originX=Math.max(0,Math.min(1024-cols,originX+dx));
      originY=Math.max(0,Math.min(1024-rows,originY+dy));
      draw();
    };
  });
  el("previewHome").onclick=()=>{originX=496;originY=500;draw();};
  el("startNew").onclick=()=>{
    if(!seed.checkValidity()) {seed.reportValidity();return;}
    start(model());
  };
  el("exportMap").onclick=()=>{
    if(!seed.checkValidity()) {seed.reportValidity();return;}
    const url=URL.createObjectURL(new Blob([JSON.stringify(model(),null,2)],{type:"application/json"}));
    const link=Object.assign(document.createElement("a"),{href:url,download:"red-meridian-map.json"});
    link.click();
    setTimeout(()=>URL.revokeObjectURL(url),1000);
  };
  el("importMap").onclick=()=>el("mapFile").click();
  el("mapFile").onchange=async event=>{
    const [file]=event.target.files;
    event.target.value="";
    if(!file) return;
    try {
      if(file.size>1_000_000) throw new Error("Map file is too large.");
      const data=JSON.parse(await file.text());
      if(!Number.isInteger(data.seed) || data.seed<-(2**31) || data.seed>=2**31 ||
        !Number.isInteger(data.abundance) || data.abundance<0 || data.abundance>200 ||
        !data.paint || Array.isArray(data.paint) || typeof data.paint!=="object") throw new Error("Invalid map file.");
      const tiles=Object.entries(data.paint);
      if(tiles.length>16384 || tiles.some(([key,id])=>!/^\d+$/.test(key) || String(Number(key))!==key ||
        Number(key)>=1024*1024 || geology.fixed.has(Number(key)) ||
        id!==null && !deposits.some(d=>d.id===id))) throw new Error("Invalid map edits.");
      seed.value=data.seed;
      abundance.value=data.abundance;
      paint={...data.paint};
      draw();
      el("menuNotice").textContent="Map design imported.";
    } catch(error) {el("menuNotice").textContent=error.message;}
  };
  draw();
}
