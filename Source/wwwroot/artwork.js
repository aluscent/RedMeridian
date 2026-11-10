"use strict";

const Artwork=(() => {
  const resources=["water","ore","food","alloy","research","silicon","titanium","brine","uranium","propellant"];
  const manifestCache=new Map(), atlasCache=new Map();
  const buildingSheets={
    planet:{columns:4,rows:3,ids:[4,27,13,15,23,24,25,26,33,28,31,30]},
    orbit:{columns:5,rows:4,ids:[1,4,5,6,7,11,14,15,20,21,22,23,24,27,29,30,32,33]}
  };
  const safe=value=>typeof value==="string" && /^[a-z0-9-]+$/.test(value);
  const environment=kind=>kind==="orbit" ? "orbit" : "planet";

  function cell(kind,key) {
    if(kind==="resource") {
      const index=resources.indexOf(key);
      return index<0 ? -1 : 27+index;
    }
    return key<=27 ? key-1 : key===33 ? 6 : key<=32 ? key+9 : -1;
  }

  async function manifest(galaxy) {
    if(!safe(galaxy)) return {};
    if(!manifestCache.has(galaxy)) manifestCache.set(galaxy,
      fetch("assets/galaxies/"+galaxy+"/artwork.json").then(response=>response.ok ? response.json() : {})
        .catch(()=>({})));
    return await manifestCache.get(galaxy) || {};
  }

  function override(galaxy,env,kind,key,entries) {
    const name=kind+"-"+key;
    return safe(galaxy) && safe(env) && safe(name) && Array.isArray(entries?.[env]) && entries[env].includes(name)
      ? "assets/galaxies/"+galaxy+"/"+env+"/"+name+".png" : null;
  }

  async function image(path) {
    const result=new Image();
    result.src=path;
    try { await result.decode(); return result; } catch { return null; }
  }

  async function resolve(galaxy,env,kind,key,entries,load,loadDefault) {
    const path=override(galaxy,env,kind,key,entries);
    const custom=path && await load(path);
    return custom || await loadDefault(env,cell(kind,key));
  }

  async function atlasSprite(file,index,columns,rows,trim=false) {
    if(index<0) return null;
    if(!atlasCache.has(file)) atlasCache.set(file,image("assets/refresh/"+file+".png"));
    const atlas=await atlasCache.get(file);
    if(!atlas) return null;
    const w=atlas.naturalWidth/columns,h=atlas.naturalHeight/rows;
    const tile=Object.assign(document.createElement("canvas"),{width:w,height:h});
    const g=tile.getContext("2d",{willReadFrequently:trim});
    g.drawImage(atlas,index%columns*w,Math.floor(index/columns)*h,w,h,0,0,w,h);
    if(!trim) return tile;

    const {width,height}=tile, pixels=g.getImageData(0,0,width,height).data;
    let left=width,top=height,right=0,bottom=0;
    for(let y=0;y<height;y++) for(let x=0;x<width;x++) {
      if(pixels[(y*width+x)*4+3]<96) continue;
      left=Math.min(left,x);right=Math.max(right,x);
      top=Math.min(top,y);bottom=Math.max(bottom,y);
    }
    if(left>right) return null;
    const cropped=Object.assign(document.createElement("canvas"),{width:right-left+1,height:bottom-top+1});
    cropped.getContext("2d").drawImage(tile,left,top,cropped.width,cropped.height,0,0,cropped.width,cropped.height);
    return cropped;
  }

  async function defaultSprite(env,index,kind,key) {
    const sheet=buildingSheets[env], dedicated=kind==="building" ? sheet.ids.indexOf(key) : -1;
    if(dedicated>=0) {
      const sprite=await atlasSprite(env+"-structures",dedicated,sheet.columns,sheet.rows,true);
      if(sprite) return sprite;
    }
    return atlasSprite(env,index,7,6);
  }

  async function sprite(galaxy,env,kind,key) {
    return resolve(galaxy,env,kind,key,await manifest(galaxy),image,
      (environment,index)=>defaultSprite(environment,index,kind,key));
  }

  return {environment,cell,override,resolve,sprite};
})();
if(typeof module!=="undefined") module.exports=Artwork;
