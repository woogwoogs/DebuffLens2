# DebuffLens2

## Display styles

The first settings choice is **Display style**. It has exactly two options: **Compact Icons** for the smallest, fastest combat read, and **Detailed Icons** for the vertically stacked icon-and-text learning view. Detailed Icons always uses a vertical layout; it is the former icon-only layout with the name and curated consequence placed beside the icon.

Compact Icons starts as framed effect icons only. In **Vertical layout**, its optional names and descriptions can be enabled for the same aligned icon-and-text view without changing display style. The optional **Show radial dial** draws one uninterrupted smooth solid dark elapsed sector counter-clockwise over the icon—without a perimeter progress ring, timer hand, or internal radial tick lines. Icons use a clean black outline; priority colour remains in the icon and alert treatment. Real runtime `MaxTime` is preferred; when ExileCore2 supplies only `Timer`, the optional observed-duration fallback uses the highest live timer seen during that application and discards it when the effect ends. It never fabricates timing for persistent effects.

**Detailed Icons** uses the icon-and-text warning list. Icons stay aligned in one column; each compact name and curated HC consequence sits to its right. **Show names** and **Show descriptions** remain independent controls, and long text wraps inside the configurable **Description column width** instead of being truncated or widening the HUD indefinitely.

**Application effects** only changes a newly applied effect: Critical briefly pulses with a red border and Major gets a short orange glow. Minor effects never animate. Once the 0.9-second effect finishes, all icons return to clean black frames. With this option off, Critical and Major use small red/orange upper-left triangles; Minor has no priority marker.

