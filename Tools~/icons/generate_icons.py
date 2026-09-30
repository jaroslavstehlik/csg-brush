"""
Draws the CSG Brush editor icons: flat single-colour glyphs in the style of Unity's own, 3D shapes from one shared
three-quarter view. Each icon is drawn once as a coverage mask (supersampled, then scaled down) and written twice:
Name.png for the light editor theme and d_Name.png for the dark one (Unity picks d_ on the dark theme), 32 x 32 so it
is sharp at 16 points on high-density screens.

    python3 -m venv .venv && .venv/bin/pip install -r requirements.txt
    .venv/bin/python generate_icons.py              # writes ../../Brushes/Editor/Icons
    .venv/bin/python generate_icons.py --sheet out.png   # also a contact sheet to look at
"""
import argparse
import math
import os
import uuid

from PIL import Image, ImageDraw

SIZE = 32                     # written size in pixels (16 points at 2x)
SUPER = 8                     # supersampling factor
UNIT = SIZE * SUPER / 16.0    # pixels per design unit; icons are designed on a 16 x 16 grid
DARK_INK = (196, 196, 196)    # icons on Unity's dark theme
LIGHT_INK = (85, 85, 85)      # icons on the light theme
LINE = 1.0                    # regular stroke, in design units (1 point)
BOLD = 1.9                    # emphasised stroke
DIM = 0.42                    # opacity of context lines (the cube around a selected element)
FILL_TOP, FILL_LEFT, FILL_RIGHT = 0.34, 0.20, 0.10  # faces of a solid, lit from above


# ---------------------------------------------------------------- projection

K = 6.6      # edge length of the unit cube, in design units: the glyphs fill the square, as ProBuilder's do
CX, CY = 8.0, 8.0
COS30 = math.cos(math.radians(30))


def P(x, y, z):
    """A point of the unit cube [0,1]^3 (y up) in design units; +x goes right and down, +z left and down."""
    return (CX + ((x - 0.5) - (z - 0.5)) * K * COS30,
            CY + ((x - 0.5) + (z - 0.5)) * K * 0.5 - (y - 0.5) * K)


def circle(c, r, y=None, n=96, a0=0.0, a1=2 * math.pi):
    """Points of a horizontal circle (in the xz plane at height y) or arc, projected."""
    cx, cy, cz = c
    return [P(cx + r * math.cos(a0 + (a1 - a0) * i / n), cy if y is None else y, cz + r * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]


def depth(pt3):
    x, y, z = pt3
    return x + z  # larger is nearer the viewer


# ---------------------------------------------------------------- drawing on a coverage mask

class Icon:
    def __init__(self):
        self.img = Image.new("L", (SIZE * SUPER, SIZE * SUPER), 0)
        self.d = ImageDraw.Draw(self.img)

    @staticmethod
    def px(p):
        return (p[0] * UNIT, p[1] * UNIT)

    def fill(self, pts, level):
        self.d.polygon([self.px(p) for p in pts], fill=int(255 * level))

    def erase(self, pts):
        self.d.polygon([self.px(p) for p in pts], fill=0)

    def line(self, pts, width=LINE, level=1.0, closed=False, round_caps=True):
        pts = [self.px(p) for p in pts]
        if closed:
            pts = pts + [pts[0]]
        w = width * UNIT
        v = int(255 * level)
        self.d.line(pts, fill=v, width=max(1, int(round(w))), joint="curve")
        r = w / 2
        for q in ((pts[0], pts[-1]) if round_caps else ()):  # round caps
            self.d.ellipse([q[0] - r, q[1] - r, q[0] + r, q[1] + r], fill=v)

    def dashed(self, a, b, width=LINE, level=1.0, dash=1.3, gap=1.1):
        (x0, y0), (x1, y1) = a, b
        length = math.hypot(x1 - x0, y1 - y0)
        t = 0.0
        while t < length:
            t1 = min(t + dash, length)
            f0, f1 = t / length, t1 / length
            self.line([(x0 + (x1 - x0) * f0, y0 + (y1 - y0) * f0), (x0 + (x1 - x0) * f1, y0 + (y1 - y0) * f1)], width, level)
            t = t1 + gap

    def dot(self, p, r, level=1.0):
        x, y = self.px(p)
        r *= UNIT
        self.d.ellipse([x - r, y - r, x + r, y + r], fill=int(255 * level))

    def result(self):
        return self.img.resize((SIZE, SIZE), Image.LANCZOS)


# ---------------------------------------------------------------- shared parts

CUBE = {n: (x, y, z) for n, (x, y, z) in {
    "a": (0, 0, 0), "b": (1, 0, 0), "c": (1, 0, 1), "d": (0, 0, 1),
    "e": (0, 1, 0), "f": (1, 1, 0), "g": (1, 1, 1), "h": (0, 1, 1)}.items()}
VISIBLE_EDGES = ["ef", "fg", "gh", "he", "fb", "gc", "hd", "bc", "cd"]
HIDDEN_EDGES = ["ab", "ad", "ae"]


def cube_faces(ic, top=FILL_TOP, left=FILL_LEFT, right=FILL_RIGHT):
    c = {k: P(*v) for k, v in CUBE.items()}
    ic.fill([c["e"], c["f"], c["g"], c["h"]], top)
    ic.fill([c["d"], c["c"], c["g"], c["h"]], left)   # z = 1
    ic.fill([c["b"], c["c"], c["g"], c["f"]], right)  # x = 1


def cube_edges(ic, level=1.0, width=LINE):
    c = {k: P(*v) for k, v in CUBE.items()}
    for e in VISIBLE_EDGES:
        ic.line([c[e[0]], c[e[1]]], width, level)


def arrow(ic, a, b, width=LINE, head=1.9, level=1.0):
    ic.line([a, b], width, level)
    ang = math.atan2(b[1] - a[1], b[0] - a[0])
    for s in (-1, 1):
        ic.line([b, (b[0] - head * math.cos(ang + s * 0.55), b[1] - head * math.sin(ang + s * 0.55))], width, level)


def extrude_profile(ic, profile, z0, z1, fills=True):
    """A shape given as an xy profile (counter-clockwise, y up) extruded from z0 (back) to z1 (front)."""
    n = len(profile)
    if fills:
        for i in range(n):  # sides that face the viewer: +x or +y
            (x0, y0), (x1, y1) = profile[i], profile[(i + 1) % n]
            nx, ny = (y1 - y0), -(x1 - x0)  # outward normal of a counter-clockwise profile
            if nx > 1e-6 or ny > 1e-6:
                ic.fill([P(x0, y0, z0), P(x1, y1, z0), P(x1, y1, z1), P(x0, y0, z1)], FILL_TOP if ny > 1e-6 else FILL_RIGHT)
        ic.fill([P(x, y, z1) for x, y in profile], FILL_LEFT)
    for i in range(n):
        (x0, y0), (x1, y1) = profile[i], profile[(i + 1) % n]
        nx, ny = (y1 - y0), -(x1 - x0)
        ic.line([P(x0, y0, z1), P(x1, y1, z1)])  # front outline
        if nx > 1e-6 or ny > 1e-6:
            ic.line([P(x0, y0, z0), P(x1, y1, z0)])  # back edge of a visible side
    for i in range(n):  # depth edges at corners where a visible side meets a hidden one or the front face
        (xp, yp), (x0, y0), (x1, y1) = profile[i - 1], profile[i], profile[(i + 1) % n]
        vis_prev = (y0 - yp) > 1e-6 or -(x0 - xp) > 1e-6
        vis_next = (y1 - y0) > 1e-6 or -(x1 - x0) > 1e-6
        if vis_prev != vis_next or (vis_prev and vis_next and (y0 - yp) * (y1 - y0) + (x0 - xp) * (x1 - x0) < 0.99 * math.hypot(y0 - yp, x0 - xp) * math.hypot(y1 - y0, x1 - x0)):
            ic.line([P(x0, y0, z0), P(x0, y0, z1)])


def prisms(ic, items):
    """
    Solids given as floor outlines (x, z, sharp) extruded from y0 to y1, drawn face by face from back to front: each face is
    filled (which covers the lines behind it) and outlined. Walls between two non-sharp outline points are one smooth
    surface (no line between them), as on a curved wall.
    """
    faces = []
    for outline, y0, y1 in items:
        area = sum(outline[i][0] * outline[(i + 1) % len(outline)][1] - outline[(i + 1) % len(outline)][0] * outline[i][1] for i in range(len(outline)))
        n = len(outline)
        for i in range(n):
            (ax, az, asharp), (bx, bz, bsharp) = outline[i], outline[(i + 1) % n]
            dx, dz = bx - ax, bz - az
            nx, nz = (dz, -dx) if area > 0 else (-dz, dx)  # outward, for either winding
            length = math.hypot(nx, nz) or 1.0
            nx, nz = nx / length, nz / length
            if nx + nz <= 1e-6:
                continue  # faces away from the viewer
            level = (FILL_LEFT * max(nz, 0) + FILL_RIGHT * max(nx, 0)) / max(max(nz, 0) + max(nx, 0), 1e-6)
            quad = [(ax, y0, az), (bx, y0, bz), (bx, y1, bz), (ax, y1, az)]
            edges = [((ax, y0, az), (bx, y0, bz)), ((ax, y1, az), (bx, y1, bz))]
            if asharp: edges.append(((ax, y0, az), (ax, y1, az)))
            if bsharp: edges.append(((bx, y0, bz), (bx, y1, bz)))
            faces.append((sum(x + y + z for x, y, z in quad) / 4, quad, level, edges))
        top = [(x, y1, z) for x, z, _ in outline]
        faces.append((sum(x + y + z for x, y, z in top) / len(top) + 0.02, top, FILL_TOP, [(top[i], top[(i + 1) % n]) for i in range(n)]))
    for _, pts, level, edges in sorted(faces, key=lambda f: f[0]):
        ic.fill([P(*q) for q in pts], level)
        for a, b in edges:
            ic.line([P(*a), P(*b)])


def sector(r0, r1, a0, a1, cx=0.0, cz=0.0, n=16):
    """Floor outline of a ring sector around (cx, cz): outer arc, then inner arc back; corners sharp, arcs smooth."""
    outer = [(cx + r1 * math.cos(a0 + (a1 - a0) * i / n), cz + r1 * math.sin(a0 + (a1 - a0) * i / n), i in (0, n)) for i in range(n + 1)]
    inner = [(cx + r0 * math.cos(a1 + (a0 - a1) * i / n), cz + r0 * math.sin(a1 + (a0 - a1) * i / n), i in (0, n)) for i in range(n + 1)]
    return outer + inner


# ---------------------------------------------------------------- icons

def box(ic):
    prisms(ic, [([(0, 0, True), (1, 0, True), (1, 1, True), (0, 1, True)], 0, 1)])

def wedge(ic):
    # high at x = 0, the slope facing +x
    b, c, d, e, h = P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), P(0, 1, 0), P(0, 1, 1)
    ic.fill([e, b, c, h], FILL_TOP)
    ic.fill([d, c, h], FILL_LEFT)
    for s in ((e, h), (h, c), (c, b), (b, e), (h, d), (d, c)):
        ic.line(s)


def cylinder(ic, top=1.0):
    c = (0.5, 0, 0.5)
    r = 0.5
    front = circle(c, r, 0, a0=-math.pi / 4, a1=3 * math.pi / 4)      # the near half of the base
    top_ring = circle(c, r, top)
    ic.fill(front + list(reversed(circle(c, r, top, a0=-math.pi / 4, a1=3 * math.pi / 4))), FILL_LEFT)
    ic.fill(top_ring, FILL_TOP)
    ic.line(top_ring, closed=True)
    ic.line(front)
    ic.line([front[0], top_ring[int(96 * 7 / 8)]])
    ic.line([front[-1], P(*[0.5 + r * math.cos(3 * math.pi / 4), top, 0.5 + r * math.sin(3 * math.pi / 4)])])


def cone(ic):
    c = (0.5, 0, 0.5)
    r = 0.62
    apex = P(0.5, 1.12, 0.5)
    base = circle(c, r, -0.02)
    left = min(base, key=lambda p: p[0]); right = max(base, key=lambda p: p[0])
    front = [p for p in base if p[1] >= min(left[1], right[1]) - 1e-6]
    front.sort(key=lambda p: p[0])
    ic.fill([apex, right] + list(reversed(front)) + [left], FILL_LEFT)
    ic.fill([apex] + [p for p in front if p[0] > apex[0]] + [right], FILL_RIGHT)  # the shaded side
    ic.line(front)
    ic.line([left, apex, right])

def sphere(ic):
    cx, cy, r = 8.0, 8.0, 7.0
    ring = [(cx + r * math.cos(t * 2 * math.pi / 96), cy + r * math.sin(t * 2 * math.pi / 96)) for t in range(97)]
    ic.fill(ring, FILL_RIGHT)
    ic.fill([(cx + r * math.cos(t), cy + r * math.sin(t)) for t in [math.pi + i * math.pi / 48 for i in range(49)]] + [(cx + r, cy)], FILL_TOP * 0.8)
    ic.line(ring, closed=True)
    eq = [(cx + r * math.cos(t * math.pi / 48), cy + r * 0.38 * math.sin(t * math.pi / 48)) for t in range(49)]  # near half of the equator
    ic.line(eq)
    mer = [(cx + r * 0.38 * math.cos(t), cy + r * math.sin(t)) for t in [-math.pi / 2 + i * math.pi / 48 for i in range(49)]]
    ic.line(mer, level=0.7)


def linear_stairs(ic):
    # three steps climbing toward the back
    prisms(ic, [([(0, 0, True), (1 / 3, 0, True), (1 / 3, 1, True), (0, 1, True)], 0, 1),
                ([(1 / 3, 0, True), (2 / 3, 0, True), (2 / 3, 1, True), (1 / 3, 1, True)], 0, 2 / 3),
                ([(2 / 3, 0, True), (1, 0, True), (1, 1, True), (2 / 3, 1, True)], 0, 1 / 3)])

def ring_step(ic, r0, r1, a0, a1, y0, y1, n=24):
    """One step of a round stair: a ring sector from angle a0 to a1 (radians, around the y axis at x = z = 0),
    between radii r0 and r1, from height y0 to y1. Draws the faces that can face the viewer."""
    outer_top = [P(r1 * math.cos(a0 + (a1 - a0) * i / n), y1, r1 * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]
    inner_top = [P(r0 * math.cos(a0 + (a1 - a0) * i / n), y1, r0 * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]
    outer_bot = [P(r1 * math.cos(a0 + (a1 - a0) * i / n), y0, r1 * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]
    ic.fill(outer_top + list(reversed(outer_bot)), FILL_LEFT)            # outer wall
    riser = [P(r0 * math.cos(a0), y0, r0 * math.sin(a0)), P(r1 * math.cos(a0), y0, r1 * math.sin(a0)), P(r1 * math.cos(a0), y1, r1 * math.sin(a0)), P(r0 * math.cos(a0), y1, r0 * math.sin(a0))]
    ic.fill(riser, FILL_RIGHT)
    ic.fill(outer_top + list(reversed(inner_top)), FILL_TOP)             # tread
    ic.line(outer_top); ic.line(inner_top); ic.line(outer_bot)
    ic.line([inner_top[0], outer_top[0]]); ic.line([inner_top[-1], outer_top[-1]])
    ic.line([outer_top[0], outer_bot[0]]); ic.line([riser[0], riser[3]]); ic.line([riser[0], riser[1]])


def curved_stairs(ic):
    # a quarter turn around the far corner, each step higher, climbing from the right toward the left
    steps = 4
    items = []
    for k in range(steps):
        a0 = (k / steps) * (math.pi / 2); a1 = ((k + 1) / steps) * (math.pi / 2)
        items.append((sector(0.34, 1.0, a0, a1), 0, (k + 1) / steps))
    prisms(ic, items)

def spiral_stairs(ic):
    # a column with thin treads winding up around it, half a turn apart in height so each is seen
    column = [(0.5 + 0.1 * math.cos(t * math.pi / 12), 0.5 + 0.1 * math.sin(t * math.pi / 12), False) for t in range(24)]
    items = [(column, -0.06, 1.1)]
    for k in range(5):
        a = math.radians(-45 + k * 72)
        y = -0.05 + k * 0.27
        items.append((sector(0.1, 0.6, a - 0.5, a + 0.5, 0.5, 0.5, 8), y, y + 0.07))
    prisms(ic, items)

def arch(ic):
    # what the Arch brush makes: a band of segments between an outer and an inner half ellipse, standing on the floor;
    # drawn face-on (an arch reads only that way) with its depth offset up and to the right
    cx, floor = 6.8, 14.5
    ox, oy, ix, iy = 5.4, 9.4, 2.6, 6.1     # outer and inner radii (x, y)
    dx, dy = 2.6, -2.6
    segments, n = 5, 48
    outer = [(cx - ox * math.cos(math.pi * i / n), floor - oy * math.sin(math.pi * i / n)) for i in range(n + 1)]
    inner = [(cx - ix * math.cos(math.pi * i / n), floor - iy * math.sin(math.pi * i / n)) for i in range(n + 1)]
    band = outer + list(reversed(inner))
    back = lambda pts: [(x + dx, y + dy) for x, y in pts]
    ic.fill(outer + list(reversed(back(outer))), FILL_TOP)          # the outer curve, seen from above
    ic.line(back(outer))
    ic.fill(inner + [inner[0]], FILL_RIGHT)                          # under the arch: its inner face shows on the left
    ic.erase(back(inner)[1:-1] + [(inner[-1][0] + dx, floor + 1), (inner[0][0] + dx, floor + 1)])
    ic.fill(band, FILL_LEFT)
    ic.line(band, closed=True)
    for k in range(1, segments):                                     # the segments it is built from
        t = math.pi * k / segments
        ic.line([(cx - ox * math.cos(t), floor - oy * math.sin(t)), (cx - ix * math.cos(t), floor - iy * math.sin(t))], LINE * 0.8, 0.6)


def door(ic):
    # what the Door brush makes: a frame of two sides and a top around an opening down to the floor, face-on
    x0, x1, top, bottom = 1.5, 11.8, 4.4, 14.9
    dx, dy = 3.0, -3.0
    side, lintel = 2.7, 2.9
    opening = [(x0 + side, bottom), (x0 + side, top + lintel), (x1 - side, top + lintel), (x1 - side, bottom)]
    front = [(x0, bottom)] + opening + [(x1, bottom), (x1, top), (x0, top)]
    ic.fill([(x0, top), (x1, top), (x1 + dx, top + dy), (x0 + dx, top + dy)], FILL_TOP)
    ic.fill([(x1, top), (x1, bottom), (x1 + dx, bottom + dy), (x1 + dx, top + dy)], FILL_RIGHT)
    ic.fill(opening, FILL_RIGHT)                                                     # the inside of the left side
    ic.erase([(x0 + side + dx, bottom + 1), (x0 + side + dx, top + lintel + dy), (x1 - side + dx, top + lintel + dy), (x1 - side + dx, bottom + 1)])
    ic.fill(front, FILL_LEFT)
    ic.line(front, closed=True)
    ic.line([(x0, top), (x0 + dx, top + dy), (x1 + dx, top + dy), (x1, top)])
    ic.line([(x1 + dx, top + dy), (x1 + dx, bottom + dy), (x1, bottom)])

def window(ic):
    # what the Window brush cuts: a wall with an opening above the floor, face-on like the door
    x0, x1, top, bottom = 1.5, 11.8, 4.4, 14.9
    dx, dy = 3.0, -3.0
    hx0, hx1, hy0, hy1 = x0 + 3.0, x1 - 3.0, top + 3.0, bottom - 3.6
    hole = [(hx0, hy1), (hx0, hy0), (hx1, hy0), (hx1, hy1)]
    ic.fill([(x0, top), (x1, top), (x1 + dx, top + dy), (x0 + dx, top + dy)], FILL_TOP)
    ic.fill([(x1, top), (x1, bottom), (x1 + dx, bottom + dy), (x1 + dx, top + dy)], FILL_RIGHT)
    ic.fill(hole, FILL_RIGHT)                                                        # the inside of the hole's left and bottom
    ic.erase([(hx0 + dx, hy1 + dy), (hx0 + dx, hy0 + dy), (hx1 + dx, hy0 + dy), (hx1 + dx, hy1 + dy)])
    for strip in ([(x0, top), (x1, top), (x1, hy0), (x0, hy0)], [(x0, hy1), (x1, hy1), (x1, bottom), (x0, bottom)],
                  [(x0, hy0), (hx0, hy0), (hx0, hy1), (x0, hy1)], [(hx1, hy0), (x1, hy0), (x1, hy1), (hx1, hy1)]):
        ic.fill(strip, FILL_LEFT)                                                    # the wall's face around the hole
    ic.line([(x0, bottom), (x0, top), (x1, top), (x1, bottom)], closed=True)
    ic.line(hole, closed=True)
    ic.line([(x0, top), (x0 + dx, top + dy), (x1 + dx, top + dy), (x1, top)])
    ic.line([(x1 + dx, top + dy), (x1 + dx, bottom + dy), (x1, bottom)])

def floor_plan(ic):
    # an outline drawn on the floor, walls rising along its far edges
    lo = [P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)]
    ic.fill(lo, FILL_TOP * 0.5)
    t, h = 0.15, 0.7
    prisms(ic, [([(0, 0, True), (1, 0, True), (1, t, True), (t, t, True), (t, 1, True), (0, 1, True)], 0, h)])
    ic.line([P(1, 0, t), P(1, 0, 1), P(t, 0, 1)])

def mode_vertex(ic):
    cube_edges(ic, DIM)
    for k in "bcdefgh":
        ic.dot(P(*CUBE[k]), 0.95)


def mode_edge(ic):
    cube_edges(ic, DIM)
    ic.line([P(*CUBE["g"]), P(*CUBE["c"])], BOLD * 1.25, round_caps=False)  # one edge: the nearest vertical, heavy enough to read at 16 points

def mode_face(ic):
    c = {k: P(*v) for k, v in CUBE.items()}
    ic.fill([c["e"], c["f"], c["g"], c["h"]], 1.0)  # the top face, selected
    cube_edges(ic, DIM)
    ic.line([c["e"], c["f"], c["g"], c["h"]], LINE, 1.0, closed=True)


def edit_brush(ic):
    # the edit context: a solid brush with its corners marked, ready to be edited
    cube_faces(ic)
    cube_edges(ic)
    for k in "bcdefgh":
        ic.dot(P(*CUBE[k]), 0.9)


def select_hidden(ic):
    # see-through: the edges behind the cube are drawn too, dashed
    c = {k: P(*v) for k, v in CUBE.items()}
    for e in HIDDEN_EDGES:
        ic.dashed(c[e[0]], c[e[1]], LINE, 0.8)
    ic.dot(c["a"], 1.1)
    cube_edges(ic)


def drag_rect(ic):
    x0, y0, x1, y1 = 1.2, 2.4, 14.8, 13.6
    for a, b in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
        ic.dashed(a, b, LINE, 1.0, dash=1.6, gap=1.2)
    for p in ((5.2, 6.2), (10.8, 7.0), (7.4, 10.4)):
        ic.dot(p, 1.15)


def handle_element(ic):
    # a tilted face and its normal: the gizmo follows the selection
    face = [P(0.05, 0.25, 0.25), P(0.95, 0.05, 0.25), P(0.95, 0.05, 1.0), P(0.05, 0.25, 1.0)]
    ic.fill(face, FILL_TOP * 1.4)
    ic.line(face, closed=True)
    cx = sum(p[0] for p in face) / 4; cy = sum(p[1] for p in face) / 4
    arrow(ic, (cx, cy), (cx + 1.2, cy - 6.2), LINE, 2.0)
    ic.dot((cx, cy), 1.0)


def brush(ic):
    # a box with its near top corner cut away: a brush, and what CSG does to it
    t = 0.5
    prisms(ic, [([(0, 0, True), (1, 0, True), (1, 1, True), (0, 1, True)], 0, t),
                ([(0, 0, True), (1, 0, True), (1, t, True), (t, t, True), (t, 1, True), (0, 1, True)], t, 1)])

def extrude(ic):
    # a floor face pulled up: the face, its extrusion dashed, and the direction
    lo = [P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)]
    hi = [P(0, 0.8, 0), P(1, 0.8, 0), P(1, 0.8, 1), P(0, 0.8, 1)]
    ic.fill(lo, FILL_TOP * 1.8)
    ic.line(lo, closed=True)
    for i in range(4):
        ic.dashed(hi[i], hi[(i + 1) % 4], LINE, 0.75)
    for i in (1, 2, 3):
        ic.dashed(lo[i], hi[i], LINE, 0.75)
    c = P(0.5, 0, 0.5); t = P(0.5, 0.95, 0.5)
    arrow(ic, c, t, BOLD * 0.8, 2.2)


ICONS = {
    # create tools (names match BrushShape)
    "Box": box, "Wedge": wedge, "Cylinder": cylinder, "Cone": cone, "Sphere": sphere,
    "Stairs": linear_stairs, "CurvedStairs": curved_stairs, "SpiralStairs": spiral_stairs, "Arch": arch, "Door": door, "Window": window, "FloorPlan": floor_plan,
    # edit toolbar
    "EditBrush": edit_brush, "Mode_Vertex": mode_vertex, "Mode_Edge": mode_edge, "Mode_Face": mode_face,
    "SelectHidden": select_hidden, "DragRect": drag_rect, "ToolHandleElement": handle_element,
    # overlays and the Brush component
    "Brush": brush, "Extrude": extrude,
}


# Unity import settings for editor UI icons: no mipmaps, no compression, alpha is transparency, clamped.
META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  isReadable: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  alphaUsage: 1
  alphaIsTransparency: 1
  textureType: 2
  textureShape: 1
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def write_meta(png_path):
    """A .meta beside the icon, unless one exists. The GUID comes from the file name, so every machine writes the same."""
    meta_path = png_path + ".meta"
    if os.path.exists(meta_path):
        return
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "digital.dream.csgbrush/Brushes/Editor/Icons/" + os.path.basename(png_path)).hex
    with open(meta_path, "w") as f:
        f.write(META.format(guid=guid))


def tinted(mask, ink):
    out = Image.new("RGBA", mask.size, ink + (0,))
    out.putalpha(mask)
    return out


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=os.path.normpath(os.path.join(here, "..", "..", "Brushes", "Editor", "Icons")))
    parser.add_argument("--sheet", help="also write a contact sheet of every icon on both themes")
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)
    masks = {}
    for name, draw in ICONS.items():
        ic = Icon(); draw(ic)
        masks[name] = ic.result()
        border = [masks[name].getpixel((x, y)) for x in range(SIZE) for y in range(SIZE) if x in (0, SIZE - 1) or y in (0, SIZE - 1)]
        if max(border) > 40:
            print("warning: %s reaches the edge of the square and may look clipped" % name)
        for file, ink in ((name + ".png", LIGHT_INK), ("d_" + name + ".png", DARK_INK)):
            path = os.path.join(args.out, file)
            tinted(masks[name], ink).save(path)
            write_meta(path)
    if args.sheet:
        scale, pad = 4, 12
        cols = len(masks)
        sheet = Image.new("RGBA", (cols * (SIZE * scale + pad) + pad, 2 * (SIZE * scale + pad) + pad + 2 * (SIZE + pad)), (0, 0, 0, 255))
        for row, (bg, ink) in enumerate((((56, 56, 56), DARK_INK), ((200, 200, 200), LIGHT_INK))):
            top = row * (SIZE * scale + pad) + pad
            ImageDraw.Draw(sheet).rectangle([0, top - pad // 2, sheet.width, top + SIZE * scale + pad // 2], fill=bg + (255,))
            for i, m in enumerate(masks.values()):
                big = tinted(m, ink).resize((SIZE * scale, SIZE * scale), Image.NEAREST)
                sheet.alpha_composite(big, (pad + i * (SIZE * scale + pad), top))
        # actual size (16 points = 32 px), both themes
        for row, (bg, ink) in enumerate((((56, 56, 56), DARK_INK), ((200, 200, 200), LIGHT_INK))):
            top = 2 * (SIZE * scale + pad) + pad + row * (SIZE + pad)
            ImageDraw.Draw(sheet).rectangle([0, top - pad // 2, sheet.width, top + SIZE + pad // 2], fill=bg + (255,))
            for i, m in enumerate(masks.values()):
                sheet.alpha_composite(tinted(m, ink), (pad + i * (SIZE * scale + pad), top))
        sheet.save(args.sheet)
    print("wrote %d icons x 2 themes to %s" % (len(masks), args.out))


if __name__ == "__main__":
    main()
