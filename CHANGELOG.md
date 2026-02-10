
## **v0.3.0 – v0.3.4 Update Logs**

### **1. Core Logic: Intelligent Misalignment & Ghost Detection**

* **Ghost Detection:** The system can now identify "Ghost IDs"—players who appear in the voice room but are absent from the game room (typically a stale connection left over after a player disconnects and rejoins).
* **Smart Attribution Inference:** When a player "disconnects" from the voice list while a "Ghost ID" exists in the room, the system no longer simply marks them with a red **[Disconnected]** status. Instead, it assigns a pale green **[Misaligned]** status. This accurately reflects the reality: "The ID is mismatched, but the player can still communicate normally."
* **Enhanced Name Restoration:** Optimized caching logic now prioritizes PUN (Photon Unity Networking) real-time nicknames. This prevents IDs from defaulting to "Player X" during misalignment episodes.

### **2. UI Reconstruction**

* **Merged Status Bar:** The bottom statistics bar has been removed and integrated into the top header.
* **Format:** `Active (Yellow) + Misaligned (Pale Green) / Total (Green) (n players misaligned)`
* *Example: 8+2/10 (2 players ID misaligned)*


* **Visual Grading:**
* **[Misaligned]** status uses a low-saturation pale green (`#b2d3b2`) to distinguish it from healthy connections without causing "false alarms."
* **[Connecting]** status now includes a **25-second grace period**. It displays as Yellow initially and only turns Red if the timeout is exceeded.


* **Connection Step Visualization:** The UI now displays real-time progress, such as "Verifying..." and "Joining...", rather than just a binary "Connected/Disconnected" state.

### **3. Configuration & Interaction**

* **Config Type Changes (v0.3.4):** The UI Position setting has been changed from a Boolean (True/False) to a more intuitive dropdown string menu (**Left/Right**).
* **Default Parameter Adjustments:** * Default **Font Size** adjusted to **21**.
* **Max Name Length** adjusted to **26**.


* **Virtual Player Testing:** This option has been moved back to the "Advanced & Debugging" category. The default test name has been changed to an ultra-long string to stress-test UI overflow handling.

### **4. Protocols & Debugging**

* **Version Broadcasting:** The communication protocol has been upgraded to include the Mod version number within status synchronization packets.
* **Enhanced Dump (Alt+J):** The debug console now displays player **IP addresses**, **Mod versions**, and explicitly tags **[Ghost]** statuses. Additionally, a bug where players could not see themselves in the Dump log has been fixed.
