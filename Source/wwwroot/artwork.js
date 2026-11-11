"use strict";

const Artwork=(() => {
  const resources=["water","ore","food","alloy","research","silicon","titanium","brine","uranium","propellant",
    "power-margin","battery","colonists"];
  const manifestCache=new Map(), atlasCache=new Map();
  const buildingSheets={
    planet:{file:"planets-structures",columns:4,rows:3,ids:[4,27,13,15,23,24,25,26,33,28,31,30]},
    orbit:{file:"orbit-structures",columns:5,rows:4,ids:[1,4,5,6,7,11,14,15,20,21,22,23,24,27,29,30,32,33]}
  };
  const safe=value=>typeof value==="string" && /^[a-z0-9-]+$/.test(value);
  const environment=kind=>kind==="orbit" ? "orbit" : "planet";
  const hasPlanetStructure=id=>buildingSheets.planet.ids.includes(id);
  const hasOrbitStructure=id=>buildingSheets.orbit.ids.includes(id);

  function planetBuildingCell(id) {
    if(!Number.isInteger(id) || id<1 || id>33) return -1;
    return id===33 ? 6 : id-1;
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
    return custom || await loadDefault(env,kind,key);
  }

  async function atlasSprite(file,index,columns,rows) {
    if(index<0) return null;
    if(!atlasCache.has(file)) atlasCache.set(file,image("assets/"+file+".png"));
    const atlas=await atlasCache.get(file);
    if(!atlas) return null;
    const w=atlas.naturalWidth/columns,h=atlas.naturalHeight/rows;
    if(!Number.isInteger(w) || !Number.isInteger(h) || index>=columns*rows) return null;
    const tile=Object.assign(document.createElement("canvas"),{width:w,height:h});
    const g=tile.getContext("2d",{willReadFrequently:true});
    g.drawImage(atlas,index%columns*w,Math.floor(index/columns)*h,w,h,0,0,w,h);
    const {width,height}=tile, pixels=g.getImageData(0,0,width,height).data;
    let left=width,top=height,right=0,bottom=0;
    for(let y=0;y<height;y++) for(let x=0;x<width;x++) {
      if(!pixels[(y*width+x)*4+3]) continue;
      left=Math.min(left,x);right=Math.max(right,x);
      top=Math.min(top,y);bottom=Math.max(bottom,y);
    }
    if(left>right) return null;
    const cropped=Object.assign(document.createElement("canvas"),{width:right-left+1,height:bottom-top+1});
    cropped.getContext("2d").drawImage(tile,left,top,cropped.width,cropped.height,0,0,cropped.width,cropped.height);
    return cropped;
  }

  async function defaultSprite(env,kind,key) {
    if(kind==="resource") return resources.includes(key) ? image("assets/resource-"+key+".png") : null;
    const sheet=buildingSheets[env], dedicated=sheet.ids.indexOf(key);
    if(dedicated>=0) {
      const sprite=await atlasSprite(sheet.file,dedicated,sheet.columns,sheet.rows);
      if(sprite) return sprite;
    }
    return env==="planet" ? atlasSprite("planet-buildings",planetBuildingCell(key),8,4) : null;
  }

  async function sprite(galaxy,env,kind,key) {
    return resolve(galaxy,env,kind,key,await manifest(galaxy),image,defaultSprite);
  }

  return {environment,hasPlanetStructure,hasOrbitStructure,override,resolve,sprite};
})();
if(typeof module!=="undefined") module.exports=Artwork;
