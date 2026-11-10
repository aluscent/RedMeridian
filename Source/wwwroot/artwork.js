"use strict";

const Artwork=(() => {
  const resources=["water","ore","food","alloy","research","silicon","titanium","brine","uranium","propellant"];
  const manifestCache=new Map(), atlasCache=new Map();
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

  async function atlasSprite(env,index) {
    if(index<0) return null;
    if(!atlasCache.has(env)) atlasCache.set(env,image("assets/refresh/"+env+".png"));
    const atlas=await atlasCache.get(env);
    if(!atlas) return null;
    const w=atlas.naturalWidth/7,h=atlas.naturalHeight/6;
    const tile=Object.assign(document.createElement("canvas"),{width:w,height:h});
    tile.getContext("2d").drawImage(atlas,index%7*w,Math.floor(index/7)*h,w,h,0,0,w,h);
    return tile;
  }

  async function sprite(galaxy,env,kind,key) {
    return resolve(galaxy,env,kind,key,await manifest(galaxy),image,atlasSprite);
  }

  return {environment,cell,override,resolve,sprite};
})();
if(typeof module!=="undefined") module.exports=Artwork;
