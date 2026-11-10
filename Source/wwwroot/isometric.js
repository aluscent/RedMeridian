"use strict";

const Isometric=(() => {
  // Vertical scale and shear corrections for the built-in 2:1 surface art.
  const surfaceProjection={25:[1.10,.05],26:[1.02,.08],27:[.98,.18]};
  function project(x,y,scale,offsetX=0,offsetY=0) {
    return [offsetX+(x-y)*48*scale,offsetY+(x+y)*24*scale];
  }

  function footprint(ctx,screen,x,y,w,h,color,stroke) {
    Sprites.polygon(ctx,[screen(x,y),screen(x+w,y),screen(x+w,y+h),screen(x,y+h)],color,stroke);
  }

  function place(screen,scale,type,x,y,w,h) {
    const [px,py]=screen(x+(w-1)/2,y+(h-1)/2);
    const spriteScale=scale*(w+h)/2;
    // Align the sprite bottom vertically with the front corner of the footprint.
    return [px,py+(w+h+2)*12*scale-50*spriteScale,type,x,y,spriteScale,w,h,x+y+w+h];
  }

  const depth=(a,b)=>a[8]-b[8] || a[3]-b[3];

  function drawFoundation(ctx,screen,kind,[,,,x,y,s,w,h]) {
    if(w===1 && h===1) return;
    const rear=screen(x,y),right=screen(x+w,y),front=screen(x+w,y+h),left=screen(x,y+h);
    const edge=6*s/(w+h), orbit=kind==="orbit";
    const down=([px,py])=>[px,py+edge];
    Sprites.polygon(ctx,[left,front,down(front),down(left)],orbit?"#24303e":"#514035");
    Sprites.polygon(ctx,[front,right,down(right),down(front)],orbit?"#1c2531":"#443428");
    Sprites.polygon(ctx,[rear,right,front,left],orbit?"#425367":"#78614f",orbit?"#6f849a":"#9c7d62");
    const inset=.09;
    footprint(ctx,screen,x+inset,y+inset,w-inset*2,h-inset*2,"#00000000",orbit?"#526579":"#896f59");
  }

  function drawBuilding(ctx,sprite,[px,py,,,,s]) {
    ctx.drawImage(sprite,px-48*s,py-50*s,96*s,100*s);
  }

  function shade(x,y) {
    const grain=Math.imul(x+19,374761393)^Math.imul(y+13,668265263);
    const ridge=Math.sin(x*.17+y*.10)+Math.sin(y*.21-x*.08);
    return Math.min(7,Math.max(0,Math.floor(4+ridge*1.5+(grain>>>28)/7-1)));
  }

  function drawGround(ctx,px,py,shade,orbit,tiles,depositIndex=0) {
    if(orbit) Sprites.polygon(ctx,[[px,py],[px+48,py+24],[px,py+48],[px-48,py+24]],
      shade%2?"#2e394a":"#344153","#53647b");
    else ctx.drawImage(tiles[shade+depositIndex*8],px-49,py);
  }

  async function loadSprite(building,path=building.image,source=null,projection=[1,0]) {
    const template=()=>Sprites.buildings[Math.max(0,Sprites.art.indexOf(building.art))];
    if(!path && !source) return template();
    let image=source;
    if(!image) {
      image=new Image();
      image.src=path;
      try { await image.decode(); }
      catch { return template(); }
    }
    const density=Math.max(2,building.footprint.width,building.footprint.height);
    const sprite=Object.assign(document.createElement("canvas"),{width:96*density,height:100*density});
    const iw=image.naturalWidth || image.width, ih=image.naturalHeight || image.height;
    const [rise,tilt]=projection;
    const fit=Math.min(sprite.width/iw,sprite.height/(ih*rise+iw*Math.abs(tilt)));
    const g=sprite.getContext("2d");
    g.imageSmoothingQuality="high";
    g.setTransform(fit,tilt*fit,0,rise*fit,(sprite.width-iw*fit)/2,
      sprite.height-(ih*rise+iw*Math.max(0,tilt))*fit);
    g.drawImage(image,0,0);
    return sprite;
  }

  async function loadBuilding(building,kind,galaxy) {
    const env=Artwork.environment(kind);
    const source=await Artwork.sprite(galaxy,env,"building",building.id);
    const projection=env==="planet" && source instanceof HTMLCanvasElement ? surfaceProjection[building.id] : undefined;
    return loadSprite(building,building.environmentImages?.[env] ?? building.image,source,projection);
  }

  return {project,footprint,place,depth,drawFoundation,drawBuilding,shade,drawGround,loadSprite,loadBuilding};
})();
if(typeof module!=="undefined") module.exports=Isometric;
