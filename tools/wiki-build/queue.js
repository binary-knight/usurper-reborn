"use strict";
// Operator tool for a copied audit database or an explicitly selected database.
// This is not loaded by the production web process and makes no Discord calls.
const Database = require("better-sqlite3");
const [file, action, id, status, ...reason] = process.argv.slice(2);
try {
  if (!file || !["list", "show", "review"].includes(action))
    throw Error(
      "Usage: node queue.js <database> list | show <id> | review <id> <drafted|approved|rejected> <outcome>",
    );
  const db = new Database(file, {
    readonly: action !== "review",
    fileMustExist: true,
  });
  try {
    if (action === "list")
      console.log(
        JSON.stringify(
          db
            .prepare(
              "SELECT id, created_at, status, text FROM wiki_suggestions WHERE status = 'pending' ORDER BY id LIMIT 100",
            )
            .all(),
          null,
          2,
        ),
      );
    else {
      if (!/^\d+$/.test(id || ""))
        throw Error("A numeric suggestion ID is required");
      if (action === "show")
        console.log(
          JSON.stringify(
            db.prepare("SELECT * FROM wiki_suggestions WHERE id = ?").get(id),
            null,
            2,
          ),
        );
      else {
        const outcome = reason.join(" ");
        if (
          !["drafted", "approved", "rejected"].includes(status) ||
          !outcome ||
          outcome.length > 2000
        )
          throw Error(
            "Provide a review status and an outcome up to 2000 characters",
          );
        const changed = db
          .prepare(
            "UPDATE wiki_suggestions SET status = ?, outcome = ?, reviewed_at = ? WHERE id = ?",
          )
          .run(status, outcome, Date.now(), id);
        if (!changed.changes) throw Error("Suggestion not found");
        console.log(`Recorded ${status} outcome for suggestion ${id}`);
      }
    }
  } finally {
    db.close();
  }
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
