"use strict";
// Loopback-only preview server for browser checks, never a production service.
const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "../../web");
http
  .createServer((req, res) => {
    try {
      let relative = decodeURIComponent(
        new URL(req.url, "http://127.0.0.1").pathname,
      ).slice(1);
      let file = path.resolve(root, relative);
      if (!file.startsWith(root + path.sep)) throw Error("Not found");
      if (fs.statSync(file).isDirectory()) file = path.join(file, "index.html");
      const types = {
        ".html": "text/html",
        ".js": "text/javascript",
        ".css": "text/css",
        ".json": "application/json",
      };
      res.setHeader(
        "Content-Type",
        (types[path.extname(file)] || "text/plain") + "; charset=utf-8",
      );
      res.end(fs.readFileSync(file));
    } catch (_) {
      res.writeHead(404);
      res.end("Not found");
    }
  })
  .listen(4173, "127.0.0.1");
