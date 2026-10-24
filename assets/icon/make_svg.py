# Postacı icon: blue rounded tile, white envelope "on the move" (speed lines), optional red unread dot.
import sys

def svg(detailed: bool, unread: bool) -> str:
    # Small sizes drop the speed lines and use a bigger envelope with a thicker flap.
    if detailed:
        env = dict(x=76, y=80, w=140, h=100, rx=14)
        flap_w = 10
        lines = """
          <g stroke="#FFFFFF" stroke-linecap="round" stroke-width="11" opacity="0.92">
            <line x1="34" y1="104" x2="58" y2="104"/>
            <line x1="26" y1="130" x2="58" y2="130"/>
            <line x1="38" y1="156" x2="58" y2="156"/>
          </g>"""
    else:
        env = dict(x=46, y=72, w=164, h=116, rx=16)
        flap_w = 16
        lines = ""
    x, y, w, h, rx = env["x"], env["y"], env["w"], env["h"], env["rx"]
    flap = f"M{x+8},{y+10} L{x+w/2},{y+h*0.55} L{x+w-8},{y+10}"
    # The unread dot is larger on small sizes so it stays visible in the 16 px tray.
    cx, cy, outer, inner = (204, 56, 46, 34) if detailed else (194, 62, 62, 48)
    dot = f"""
      <circle cx="{cx}" cy="{cy}" r="{outer}" fill="#FFFFFF"/>
      <circle cx="{cx}" cy="{cy}" r="{inner}" fill="#E81123"/>""" if unread else ""
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#2B9AF3"/>
      <stop offset="1" stop-color="#0B5CAD"/>
    </linearGradient>
    <linearGradient id="paper" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#FFFFFF"/>
      <stop offset="1" stop-color="#E6F0FA"/>
    </linearGradient>
  </defs>
  <rect x="12" y="12" width="232" height="232" rx="56" fill="url(#bg)"/>
  {lines}
  <rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="url(#paper)"/>
  <path d="{flap}" fill="none" stroke="#0B5CAD" stroke-width="{flap_w}" stroke-linecap="round" stroke-linejoin="round"/>
  {dot}
</svg>"""

for detailed in (True, False):
    for unread in (False, True):
        name = f"postaci{'-unread' if unread else ''}{'' if detailed else '-small'}.svg"
        open(name, "w").write(svg(detailed, unread))
