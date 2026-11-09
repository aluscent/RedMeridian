"use strict";

// Draw each model once at a fixed scale, then reuse its sprite on every matching tile.
const Sprites = (() => {
  const make = (width, height, paint) => {
    const canvas = Object.assign(document.createElement("canvas"), {width, height});
    paint(canvas.getContext("2d"));
    return canvas;
  };
  function polygon(ctx, points, fill, stroke) {
    ctx.beginPath();
    points.forEach(([x,y], i) => i ? ctx.lineTo(x,y) : ctx.moveTo(x,y));
    ctx.closePath();
    ctx.fillStyle = fill;
    ctx.fill();
    if (stroke) {
      ctx.strokeStyle = stroke;
      ctx.stroke();
    }
  }
  const p = (x,y,z=0) => [(x-y)*48, (x+y)*24-z];
  function prism(ctx, x,y,w,d,z,h, colors) {
    const a = p(x,y,z+h), b = p(x+w,y,z+h), c = p(x+w,y+d,z+h), e = p(x,y+d,z+h);
    polygon(ctx, [b,c,p(x+w,y+d,z),p(x+w,y,z)], colors[1]);
    polygon(ctx, [e,c,p(x+w,y+d,z),p(x,y+d,z)], colors[2]);
    polygon(ctx, [a,b,c,e], colors[0]);
  }
  function line(ctx, points, stroke, width=1) {
    ctx.beginPath();
    points.forEach(([x,y], i) => i ? ctx.lineTo(x,y) : ctx.moveTo(x,y));
    ctx.strokeStyle = stroke;
    ctx.lineWidth = width;
    ctx.stroke();
    ctx.lineWidth = 1;
  }
  function ellipse(ctx,x,y,rx,ry,fill) {
    ctx.beginPath();
    ctx.ellipse(x,y,rx,ry,0,0,Math.PI*2);
    ctx.fillStyle = fill;
    ctx.fill();
  }
  function cylinder(ctx,x,y,r,h,body,top) {
    const gradient = ctx.createLinearGradient(x-r,0,x+r,0);
    gradient.addColorStop(0,top);
    gradient.addColorStop(1,body);
    ellipse(ctx,x,y,r,r*.45,body);
    ctx.fillStyle = gradient;
    ctx.fillRect(x-r,y-h,r*2,h);
    ellipse(ctx,x,y-h,r,r*.45,top);
    ellipse(ctx,x,y-h,r*.67,r*.28,body);
  }
  function dome(ctx,x,y,r,colors) {
    const gradient = ctx.createLinearGradient(x-r,y-r,x+r,y);
    colors.forEach((color,i) => gradient.addColorStop(i/(colors.length-1),color));
    ctx.beginPath();
    ctx.ellipse(x,y,r,r*.44,0,0,Math.PI);
    ctx.ellipse(x,y,r,r*.76,0,Math.PI,Math.PI*2);
    ctx.fillStyle = gradient;
    ctx.fill();
    ctx.strokeStyle = "#b9e5d0a0";
    ctx.stroke();
    ctx.beginPath();
    ctx.ellipse(x,y,r*.48,r*.75,0,Math.PI,Math.PI*2);
    ctx.stroke();
    ellipse(ctx,x-r*.32,y-r*.45,r*.21,r*.07,"#e0fff48f");
  }
  const steel = ["#bcb2a1","#5e6870","#85827e"];
  const dark = ["#615a53","#322e32","#473e3c"];
  const orange = ["#ffc16d","#a55735","#cc814c"];
  const blue = ["#b1e4e6","#46777f","#68a4aa"];
  const models = [
    ctx => {
      prism(ctx,.15,.29,.68,.44,0,3,dark);
      prism(ctx,.36,.43,.25,.18,3,13,steel);
      for(let i=0;i<2;i++){
        const x=.08+i*.44, w=.4, y=.25, d=.49;
        const points=[p(x,y,26),p(x+w,y,26),p(x+w,y+d,12),p(x,y+d,12)];
        polygon(ctx,points,"#23547b","#9bb9cd");
        for(let j=1;j<4;j++) line(ctx,[p(x+j*w/4,y,26),p(x+j*w/4,y+d,12)],"#6c9db6");
        line(ctx,[p(x,y+d/2,19),p(x+w,y+d/2,19)],"#6c9db6");
      }
    },
    ctx => {
      prism(ctx,.12,.12,.76,.76,0,5,dark);
      prism(ctx,.51,.17,.29,.51,5,8,orange);
      const top=p(.3,.45,34), left=p(.15,.6,5), right=p(.52,.6,5);
      line(ctx,[left,top,right], "#ffc078",3);
      line(ctx,[p(.21,.6,14),p(.44,.6,14),p(.25,.53,24),p(.38,.53,24)],"#db965e",2);
      line(ctx,[top,p(.3,.45,6)],"#b5b2a3",2);
      prism(ctx,.51,.63,.30,.20,5,3,steel);
    },
    ctx => {
      prism(ctx,.13,.13,.73,.73,0,5,steel);
      cylinder(ctx,13,24,10,15,"#426977","#a3d7df");
      cylinder(ctx,-7,30,10,22,"#46788b","#a9e3e8");
      line(ctx,[[-7,8],[-7,1],[12,11],[12,14]],"#d7e1d9",2);
      prism(ctx,.26,.69,.29,.15,5,5,blue);
    },
    ctx => {
      prism(ctx,.12,.14,.76,.72,0,5,steel);
      prism(ctx,.21,.25,.61,.53,5,5,["#9fb56c","#506944","#6b8151"]);
      for(let i=0;i<3;i++) {
        const [x,y]=p(.69-i*.17,.48,11);
        dome(ctx,x,y,12,["#e0f2ae","#6db681","#386b53"]);
      }
    },
    ctx => {
      prism(ctx,.1,.1,.80,.80,0,4,dark);
      prism(ctx,.17,.30,.63,.48,4,13,orange);
      cylinder(ctx,9,16,7,27,"#79452e","#edab6d");
      cylinder(ctx,-4,14,6,21,"#7a503a","#c88d63");
      prism(ctx,.4,.69,.25,.14,5,5,steel);
      line(ctx,[[-12,23],[-5,26],[2,22]],"#ffc573",2);
    },
    ctx => {
      prism(ctx,.08,.12,.84,.76,0,4,dark);
      prism(ctx,.17,.22,.65,.52,4,16,steel);
      prism(ctx,.25,.3,.46,.39,20,5,["#eee4d0","#a39b87","#b7a899"]);
      prism(ctx,.55,.65,.22,.22,4,8,orange);
      for(let i=0;i<3;i++) {
        const [x,y]=p(.24+i*.14,.742,15);
        polygon(ctx,[[x,y],[x+5,y+2.5],[x+5,y+6.5],[x,y+4]],"#6de1de");
      }
    },
    ctx => {
      prism(ctx,.11,.15,.78,.7,0,4,dark);
      prism(ctx,.22,.31,.54,.47,4,13,blue);
      cylinder(ctx,9,16,8,17,"#315665","#a7e0d9");
      line(ctx,[[-4,16],[-4,-15]],"#d9e3d9",2);
      ctx.save();
      ctx.translate(-4,-13);
      ctx.rotate(-.4);
      ellipse(ctx,0,0,11,4,"#e6e2d4");
      line(ctx,[[0,0],[4,-7]],"#888781",2);
      ctx.restore();
    },
    ctx => {
      prism(ctx,.10,.12,.80,.76,0,5,dark);
      prism(ctx,.17,.29,.64,.48,5,8,orange);
      cylinder(ctx,10,21,8,25,"#723f33","#f3985f");
      cylinder(ctx,-7,24,9,33,"#613b36","#ffb273");
      ellipse(ctx,-7,-9,4,1.5,"#ffd293");
      line(ctx,[[-19,19],[-11,23],[2,16]],"#ffad66",3);
    },
    ctx => {
      prism(ctx,.09,.11,.82,.78,0,5,steel);
      dome(ctx,0,20,24,["#c8fbd7","#59cbb2","#246969"]);
      ellipse(ctx,3,23,10,3,"#a4e58b");
      prism(ctx,.56,.72,.23,.15,3,6,blue);
    },
    ctx => {
      prism(ctx,.11,.12,.78,.77,0,4,dark);
      prism(ctx,.14,.19,.65,.64,4,9,blue);
      prism(ctx,.25,.30,.43,.42,10,2,["#368baf","#244759","#3d6880"]);
      line(ctx,[[5,19],[13,23],[18,21]],"#a5e3ea",2);
      cylinder(ctx,-14,20,6,15,"#446b79","#a3d9d8");
    }
  ];
  models.push(
    ctx => {
      prism(ctx,.12,.13,.76,.75,0,4,dark);
      for(let i=0;i<3;i++) {
        prism(ctx,.18+i*.21,.27,.19,.46,4,14,blue);
        line(ctx,[p(.24+i*.21,.39,19),p(.28+i*.21,.39,19)],"#efffd5",2);
      }
    },
    ctx => {
      prism(ctx,.27,.28,.45,.44,0,5,steel);
      line(ctx,[[0,24],[0,-16]],"#ddd6c5",4);
      ellipse(ctx,0,-16,5,4,"#b4c6c4");
      line(ctx,[[0,-16],[17,-23],[20,-27]],"#f1e9d4",3);
      line(ctx,[[0,-16],[-13,-28],[-16,-31]],"#f1e9d4",3);
      line(ctx,[[0,-16],[-2,1],[-5,4]],"#f1e9d4",3);
    },
    ctx => {
      prism(ctx,.11,.14,.77,.72,0,5,dark);
      cylinder(ctx,0,23,22,19,"#637270","#bdc8a2");
      dome(ctx,0,6,18,["#e8eda2","#b3cb73","#708757"]);
      cylinder(ctx,22,31,6,15,"#505d5d","#a8aea4");
    },
    ctx => {
      prism(ctx,.20,.19,.60,.64,0,4,dark);
      dome(ctx,0,22,22,["#e7d3ad","#c49f77","#866650"]);
      prism(ctx,.57,.66,.18,.20,3,8,orange);
    },
    ctx => {
      prism(ctx,.13,.15,.74,.72,0,4,dark);
      prism(ctx,.23,.24,.55,.53,4,30,steel);
      for(let i=0;i<3;i++) prism(ctx,.19,.20,.63,.61,8+i*9,2,blue);
      prism(ctx,.33,.32,.35,.33,34,5,orange);
    },
    ctx => {
      prism(ctx,.10,.12,.8,.77,0,5,dark);
      prism(ctx,.60,.20,.22,.45,5,9,orange);
      const top=p(.35,.5,46);
      line(ctx,[p(.14,.5,5),top,p(.56,.5,5)],"#f1bb77",3);
      line(ctx,[top,p(.35,.5,4)],"#cdd3cb",3);
      for(let i=0;i<3;i++) line(ctx,[p(.2+i*.06,.5,12+i*10),p(.5-i*.06,.5,12+i*10)],"#e7a45d",2);
    },
    ctx => {
      prism(ctx,.09,.09,.82,.82,0,4,dark);
      ellipse(ctx,0,24,25,13,"#b08462");
      ellipse(ctx,0,24,19,9,"#746858");
      prism(ctx,.26,.50,.14,.16,4,15,orange);
      cylinder(ctx,4,22,7,37,"#8f9a9b","#e0e8da");
      polygon(ctx,[[4,-32],[-3,-15],[11,-15]],"#f2ede1");
      polygon(ctx,[[-3,14],[-8,24],[2,20]],"#d47755");
      polygon(ctx,[[11,14],[16,24],[6,20]],"#d47755");
    }
  );
  const art = ["solar","mine","extractor","greenhouse","refinery","habitat","lab","heat","algae","waterworks",
    "battery","wind","reactor","shelter","tower","drill","launch"];
  const buildings = models.map(model => make(96,100,ctx => {
    ctx.translate(48,50);
    ellipse(ctx,1,28,36,16,"#170c0a60");
    model(ctx);
  }));

  const shades = ["#784332","#804835","#874c38","#90533d","#955941","#9c5c42","#9c6047","#a1654b"];
  const makeTiles = deposits => Array.from({length:(deposits.length+1)*8}, (_,index) => make(98,50,ctx => {
    const shade = index % 8, deposit = Math.floor(index/8);
    polygon(ctx,[[49,0],[98,25],[49,50],[0,25]],shades[shade],"#44261c55");
    for(let i=0;i<5;i++){
      const x = 14 + ((i*27 + shade*11) % 68);
      const y = 24 + ((i*19+shade*7)%13)-6;
      if (Math.abs(x-49)/49+Math.abs(y-25)/25 < .88) {
        ellipse(ctx,x,y,2+(i%3),1,"#d98f674d");
        ellipse(ctx,x+2,y+1,2,1,"#502a245d");
      }
    }
    if (shade === 3 || shade === 5) {
      ellipse(ctx,50,24,18,8,"#64372788");
      ellipse(ctx,48,23,14,6,"#522a2066");
      line(ctx,[[33,25],[39,29],[50,31],[61,28]],"#d4825a75",2);
    }
    if (deposit) {
      const colors = deposits[deposit-1].colors;
      ctx.translate(49,0);
      prism(ctx,34/98,17/50,.14,.15,0,6,colors);
      prism(ctx,.58,.45,.14,.13,0,9,colors);
      prism(ctx,.64,.61,.10,.13,0,5,colors);
    }
  }));
  return {buildings,art,makeTiles,polygon};
})();

