# Wander Wings

**Wander Wings** stops hot air balloons and aviation club touring planes from constantly clustering at the center of the map, giving them true freedom to explore your entire city!

---

## ✈️ What It Does

In vanilla *Cities: Skylines*, no matter where hot air balloons (Parklife DLC) or touring aircraft (Sunset Harbor DLC) launch from, their internal AI hardcodes their sightseeing loiter points back toward the center of the map (`0,0`). Over time, every balloon and plane migrates to the middle tile to hang out before flying home.

**Wander Wings** patches their simulation AI to dynamically randomize their loiter and sightseeing target coordinates across your active, unlocked city tiles.

---

## ✨ Features

* **No More Map-Center Clustering:** Sightseeing aircraft scatter naturally across different districts, coastlines, and landmarks.
* **Respects Unlocked Boundaries:** Coordinates are generated strictly within your currently unlocked city area—they won't wander off into empty wilderness tiles.
* **Lightweight & High-Performance:** Intercepts vehicle AI simulation loops efficiently via Harmony without impacting game frame rates.
* **Safe to Add/Remove Anytime:** Does not modify your save file data.

---

## 🔧 Technical Details

* **Target AIs:** `BalloonAI`, `PrivatePlaneAI`

---

This is my next mod to address another one of my long-time pet peeves with the game.

I did use Gemini AI to write the code, but the idea and testing were all mine. 

- McD