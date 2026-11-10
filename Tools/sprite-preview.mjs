import { createServer } from "node:http";
import { readFile } from "node:fs/promises";

const web=new URL("../Source/wwwroot/",import.meta.url);
const routes=new Map([
  ["/",new URL("./sprite-preview.html",import.meta.url)],
  ["/preview.css",new URL("./sprite-preview.css",import.meta.url)],
  ["/preview.js",new URL("./sprite-preview.js",import.meta.url)],
  ["/config",new URL("../Source/Config/buildings.json",import.meta.url)],
  ...["sprites.js","artwork.js","isometric.js"].map(file=>["/"+file,new URL(file,web)])
]);
const types={html:"text/html; charset=utf-8",css:"text/css; charset=utf-8",js:"text/javascript; charset=utf-8",
  json:"application/json; charset=utf-8",png:"image/png"};
const port=Number(process.argv[2] ?? 5290);
if(!Number.isInteger(port) || port<1 || port>65535) throw Error("Provide a port from 1 to 65535.");
createServer(async (req,res)=>{
  const pathname=req.url.split("?")[0];
  if(req.method!=="GET") {res.writeHead(405,{Allow:"GET"}).end();return;}
  const file=routes.get(pathname) ?? (/^\/assets\/(?:[a-z0-9_-]+\/)*[a-z0-9_-]+\.(?:png|json)$/.test(pathname)
    ? new URL(pathname.slice(1),web) : null);
  if(!file) {res.writeHead(404).end();return;}
  try {
    const body=await readFile(file);
    res.writeHead(200,{"Content-Type":types[file.pathname.split(".").pop()],"Cache-Control":"no-store"}).end(body);
  } catch(error) {
    res.writeHead(error.code==="ENOENT"?404:500).end();
  }
}).listen(port,"127.0.0.1",()=>console.log("Red Meridian sprite preview: http://127.0.0.1:"+port));
