# Usurper Reborn v1.1.8

A performance release for anyone running a server. Nothing in the game
changes; the website and its dashboards stop stalling.

## Why

On the live server the site had become slow to load data. Measured, it was
worse than slow: the web service was unresponsive for roughly two thirds of
every two minutes, and it burned most of a CPU core doing it, around the
clock, whether or not anyone was looking at the page.

The statistics page reads every player's save, which averages 84 kilobytes
of JSON, and asks the database for five to seven fields from each one across
about seven passes over the whole table. Nothing indexed those fields, so
the database parsed every save from scratch, over and over. A profile of the
running service put 62 percent of the time inside SQLite's JSON parser. The
page rebuilds on a timer, and the service does one thing at a time, so while
it rebuilt, the news feed, the API and the browser terminal were all frozen.

## What changed

- **Seven indexes carry the fields the site reads**, so those queries never
  open the save blob. On the live server the worst stall fell from 83
  seconds to 7, and the service went from burning 68 percent of a core
  continuously to under 4 percent. The statistics figures themselves now
  come back in under a millisecond.
- **Servers upgrading from an older release converge.** Three of these
  indexes existed on the live server only because someone had created them
  by hand, and four more were added the same way while this was being
  diagnosed. Any index whose definition differs from this release's is
  rebuilt once at startup, so every install ends up the same, and running it
  again changes nothing.
- **API responses are compressed.** The dashboard's largest payload drops
  from 2.2 megabytes to 400 kilobytes over the wire. The live news feed is
  deliberately left uncompressed, because compressing it would delay it.
- **The web service's memory ceiling matches what it needs.** At the old
  limit it spent its life evicting the very data it was about to read again:
  nearly eleven thousand times in a single measurement. That was a genuine
  fault, though fixing it alone did not make the site fast.

## For server operators

The indexes are applied automatically the first time this version starts.
Nothing else is required, and no save data is touched.

Two files in `scripts-server/` changed and are not copied by any deploy
step: the web service unit, which now sets a 384 MB ceiling, and the nginx
config, which now compresses JSON. Copy them if you run your own server. If
you raised the ceiling by hand on a running service, persist it in the unit
file and drop the temporary override.

The release notes for this version also record a difference worth knowing
about: the deployed configuration files on a long-running server can drift
from the ones in this repository, because nothing reconciles them. The pull
request for this release lists the differences found on ours.

## A note on what was not the cause

The first diagnosis in this investigation blamed the memory ceiling, and it
was wrong. Raising it removed real thrashing and moved the worst stall by
six seconds. The cause was found by profiling the running process, and the
release notes say so because the wrong answer was convincing and the right
one was not obvious.

## Tests

1,198 passing, up from 1,194. The new ones hold the schema to these
indexes, prove the database actually uses them, prove an upgraded server
converges on the same definitions as a fresh one, and measure what the
indexes cost when saving a character: 0.38 of a millisecond.
