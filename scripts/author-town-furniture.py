#!/usr/bin/env python3
"""Rebuild the image-guided town furniture as metre-scale, UV-mapped Blender meshes.

Run with Blender 4.2 --background --python this_file -- --output <directory>.
Game books, lamps, coins and candles remain runtime native assets. Their contact
surfaces are deliberately clear. Coordinates below use Unity's X/right,Y/up,Z/back.
"""
import argparse
import math
import sys
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--only', nargs='*', help='Rebuild only named furniture assets; preserve unrelated source FBXs.')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
out = Path(args.output).resolve()
out.mkdir(parents=True, exist_ok=True)
parts = []
materials = {}
for name, color, metal in [('DarkWood', (.20, .09, .035, 1), 0),
                           ('PaleStone', (.38, .34, .27, 1), 0),
                           ('Brass', (.35, .21, .07, 1), .7),
                           ('ForgedIron', (.065, .060, .050, 1), .85),
                           ('Leather', (.07, .025, .01, 1), 0),
                           ('AltarCloth', (.16, .035, .07, 1), 0)]:
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = color
    mat.use_nodes = True
    node = mat.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = color
    node.inputs['Metallic'].default_value = metal
    node.inputs['Roughness'].default_value = .55 if metal else .8
    materials[name] = mat


def xyz(p):
    # Unity imports Blender FBX with the X handedness conversion. Compensate
    # here so asymmetric furniture and runtime anchors share the same metre frame.
    return (-p[0], -p[2], p[1])


def mesh(name, vertices, faces, material, bevel=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata([xyz(p) for p in vertices], [], faces)
    data.update()
    bm = bmesh.new(); bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data); bm.free()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(materials[material])
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    # Physical-scale box projection avoids the stretched single texel of a primitive.
    uv = data.uv_layers.new(name='FurnitureMetres')
    for face in data.polygons:
        normal = face.normal
        axis = max(range(3), key=lambda i: abs(normal[i]))
        a, b = ((1, 2), (0, 2), (0, 1))[axis]
        for li in face.loop_indices:
            p = data.vertices[data.loops[li].vertex_index].co
            uv.data[li].uv = (p[a], p[b])
    if bevel:
        mod = obj.modifiers.new('Worn eased edge', 'BEVEL')
        mod.width = bevel
        mod.segments = 3
        bpy.ops.object.modifier_apply(modifier=mod.name)
        mod = obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
        mod.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.select_set(False)
    parts.append(obj)
    return obj


def slab(name, outline, top, thickness, material='DarkWood', bevel=.012):
    n = len(outline)
    vertices = [(x, top-thickness, z) for x, z in outline] + [(x, top, z) for x, z in outline]
    faces = [tuple(reversed(range(n))), tuple(range(n, n*2))]
    faces += [(i, (i+1) % n, (i+1) % n+n, i+n) for i in range(n)]
    return mesh(name, vertices, faces, material, bevel)


def rect(name, x, z, width, depth, top, thickness, material='DarkWood', bevel=.008):
    # Clipped corners and nonparallel edges are authored, not random per reload.
    w, d, c = width/2, depth/2, min(.025, width*.12, depth*.12)
    outline = [(x-w+c,z-d), (x+w-c,z-d), (x+w,z-d+c), (x+w,z+d-c),
               (x+w-c,z+d), (x-w+c,z+d), (x-w,z+d-c), (x-w,z-d+c)]
    return slab(name, outline, top, thickness, material, bevel)


def tube(name, points, radii, material='DarkWood', sides=12):
    # Swept branch/turning section, including bent legs, mouldings and carved tracery.
    vertices, faces = [], []
    for j, p in enumerate(points):
        tangent = Vector(points[min(j+1,len(points)-1)]) - Vector(points[max(j-1,0)])
        tangent.normalize()
        reference = Vector((0,1,0)) if abs(tangent.y) < .9 else Vector((1,0,0))
        a = tangent.cross(reference).normalized()
        b = tangent.cross(a).normalized()
        for i in range(sides):
            angle = i*math.tau/sides
            vertices.append(Vector(p) + radii[j]*(math.cos(angle)*a+math.sin(angle)*b))
    for j in range(len(points)-1):
        for i in range(sides):
            faces.append((j*sides+i, j*sides+(i+1)%sides,
                          (j+1)*sides+(i+1)%sides, (j+1)*sides+i))
    faces += [tuple(reversed(range(sides))), tuple(range((len(points)-1)*sides,len(points)*sides))]
    obj = mesh(name, vertices, faces, material)
    for poly in obj.data.polygons:
        poly.use_smooth = len(poly.vertices) == 4
    return obj


def ring(name, center, radius, wire=.006, material='Brass', vertical=False):
    x,y,z = center
    pts = [(x+radius*math.cos(i*math.tau/48),
            y+radius*math.sin(i*math.tau/48) if vertical else y,
            z if vertical else z+radius*math.sin(i*math.tau/48)) for i in range(49)]
    return tube(name, pts, [wire]*49, material, 6)


def hand_turned_disc(name, profile, material, caps=True):
    """A single lathed, subtly uneven mesh rather than stacked cylinders."""
    segments = 48
    vertices, faces = [], []
    for row, (z, radius) in enumerate(profile):
        for col in range(segments):
            angle = math.tau * col / segments
            toolmarks = .0005 * math.sin(11*angle + row*.65) + .00023*math.sin(23*angle-row*.3)
            r = max(.0005, radius + toolmarks)
            vertices.append((r*math.cos(angle),r*math.sin(angle),z))
    for row in range(len(profile)-1):
        for col in range(segments):
            a=row*segments+col; b=row*segments+(col+1)%segments
            faces.append((a,b,b+segments,a+segments))
    if caps:
        faces += [tuple(reversed(range(segments))),
                  tuple(range((len(profile)-1)*segments,len(profile)*segments))]
    obj = mesh(name,vertices,faces,material)
    for polygon in obj.data.polygons: polygon.use_smooth=len(polygon.vertices)==4
    return obj


def hand_carved_crank_grip():
    # Keep the exact handle root and reach required by the physical grip collider.
    sections = 20
    vertices, faces = [], []
    for step in range(17):
        t=step/16
        x=.097+.070*t
        centre_y=-.120+.0012*math.sin(t*math.tau*1.3)
        centre_z=-.031+.0009*math.sin(t*math.tau*2.1)
        radius=.018+.009*math.sin(math.pi*t)**.8
        radius-=.002*math.exp(-((t-.14)/.055)**2)+.002*math.exp(-((t-.86)/.055)**2)
        for col in range(sections):
            angle=math.tau*col/sections
            grain=.0007*math.sin(5*angle+4*t)+.00035*math.sin(11*angle-2*t)
            r=radius+grain
            vertices.append((x,centre_y+r*math.cos(angle),centre_z+r*math.sin(angle)))
    for row in range(16):
        for col in range(sections):
            a=row*sections+col;b=row*sections+(col+1)%sections
            faces.append((a,b,b+sections,a+sections))
    faces += [tuple(reversed(range(sections))),tuple(range(16*sections,17*sections))]
    obj=mesh('Handle_DarkWood',vertices,faces,'DarkWood')
    for polygon in obj.data.polygons:polygon.use_smooth=len(polygon.vertices)==4
    return obj


def forged_crank_arm():
    """One tapered, kinked iron strap with an inlaid line and real edge relief."""
    path=[(.080,.000,.000),(.086,-.025,-.003),(.093,-.050,-.010),
          (.090,-.078,-.020),(.096,-.115,-.030)]
    vertices=[];faces=[]
    for step,(x,y,z) in enumerate(path):
        previous=path[max(0,step-1)];following=path[min(len(path)-1,step+1)]
        dy=following[1]-previous[1];dz=following[2]-previous[2]
        length=max(.0001,math.hypot(dy,dz))
        side_y,side_z=-dz/length,dy/length
        width=.015 if step in (0,4) else .0125
        for xoff,sign in ((-.005,-1),(-.005,1),(.005,1),(.005,-1)):
            vertices.append((x+xoff,y+sign*width*side_y,z+sign*width*side_z))
    for step in range(len(path)-1):
        for side in range(4):
            a=step*4+side;b=step*4+(side+1)%4
            faces.append((a,b,b+4,a+4))
    faces += [(3,2,1,0),tuple(range((len(path)-1)*4,len(path)*4))]
    mesh('Hand forged crank strap',vertices,faces,'ForgedIron',.002)
    tube('Crank engraved inset',[(x-.006,y,z-.008) for x,y,z in path],
         [.0015]*len(path),'Brass',6)
    for x,y,z in (path[1],path[3]):
        tube('Crank peened pin',[(x-.009,y,z),(x-.012,y,z)],
             [.004,.003],'Brass',10)


def turned_leg(x, z, height=.9, stone=False):
    profile = [(0,.10),(.035,.11),(.075,.09),(.12,.055),(.22,.06),
               (.28,.065),(.34,.04),(.57,.042),(.70,.07),(.77,.08),(.84,.065),(.90,.09)]
    points = [(x+.025*math.sin(y*7), y*height/.9, z+.015*math.sin(y*9)) for y,r in profile]
    tube('Hand turned support', points, [r for y,r in profile], 'PaleStone' if stone else 'DarkWood', 16)
    for y in (.12,.73):
        ring('Forged collar', (x,y,z), .061, .009)


def cloth_front(x, table):
    # The stone mensa and rootwood top have different elliptical footprints.
    # A fixed -0.38 Z edge left most of the runner unsupported above the rim.
    if table == 'priestess':
        return -.38 * math.sqrt(max(0, 1 - (x / .81) ** 2))
    if table == 'enchantress':
        return .03 - .46 * math.sqrt(max(0, 1 - (x / .86) ** 2))
    raise ValueError(table)


def drape(x, width, table, top=.955, material='AltarCloth'):
    # Every column follows the actual tabletop lip: the upper cloth rests on
    # stone/wood, while the free rows begin just outside that lip. A single
    # rectangular Z edge floated over both curved counters in headset closeup.
    vertices, faces = [], []
    for row in range(25):
        t = row/24
        for col in range(13):
            u = col/12
            xx = x+(u-.5)*width
            edge = cloth_front(xx, table)
            fold = .0015*math.sin(u*math.tau*3.2+.3)
            surface = min(1, t/.45)
            zz = edge+.145*(1-surface)-.004*surface
            zz -= .012*math.sin(u*math.tau*3)*(max(0,t-.45)/.55)
            yy = top+.0025+fold - .49*max(0,(t-.45)/.55)
            yy -= .018*max(0,(t-.84)/.16)*(1-abs(u*2-1))
            vertices.append((xx,yy,zz))
    for row in range(24):
        for col in range(12):
            i=row*13+col
            faces.append((i,i+1,i+14,i+13))
    obj=mesh('ClothRunner_%s_%s' % (round(x*100), material), vertices, faces, material)
    for poly in obj.data.polygons: poly.use_smooth = True
    bpy.context.view_layer.objects.active=obj
    mod=obj.modifiers.new('Woven cloth thickness','SOLIDIFY'); mod.thickness=.003
    bpy.ops.object.modifier_apply(modifier=mod.name)
    # Sew the relief into the moving runner hierarchy. One brass child renderer
    # per cloth is deformed by the same owner-authored edge controls at runtime;
    # the former unparented embroidery would remain fixed in midair.
    decorations=[]
    def stitched_z(xx, yy):
        u = max(0, min(1, (xx-(x-width/2))/width))
        drop = max(0, min(1, (top+.0025-yy)/.49))
        return cloth_front(xx, table)-.007-.012*math.sin(u*math.tau*3)*drop

    sun = [(x+width*.19*math.cos(i*math.tau/48),
            top-.31+width*.19*math.sin(i*math.tau/48)) for i in range(49)]
    decorations.append(tube('Runner sun embroidery',[(a,b,stitched_z(a,b)) for a,b in sun],
                            [.0025]*len(sun),'Brass',6))
    for sign in (-1,1):
        points=[vertices[row*13+(1 if sign < 0 else 11)] for row in range(25)]
        decorations.append(tube('Runner stitched border',[(a,b+.002,c-.004) for a,b,c in points],
                                [.0016]*25,'Brass',5))
    for i in range(12):
        a=i*math.tau/12
        xx0=x+width*.22*math.cos(a); yy0=top-.31+width*.22*math.sin(a)
        xx1=x+width*.28*math.cos(a); yy1=top-.31+width*.28*math.sin(a)
        decorations.append(tube('Embroidered ray',[(xx0,yy0,stitched_z(xx0,yy0)),
             (xx1,yy1,stitched_z(xx1,yy1))],[.0018]*2,'Brass',5))
    bpy.ops.object.select_all(action='DESELECT')
    for decoration in decorations: decoration.select_set(True)
    bpy.context.view_layer.objects.active=decorations[0]
    bpy.ops.object.join()
    for decoration in decorations[1:]:parts.remove(decoration)
    decorations[0].name='ClothDecoration_%s' % round(x*100)
    decorations[0].parent=obj
    decorations[0].matrix_parent_inverse=obj.matrix_world.inverted()
    decorations[0].select_set(False)
    return obj


def worn_crank_plate():
    # A hand-cut iron escutcheon has eight unequal corners and a shallow
    # hammer-bevel, unlike the previous perfectly circular annulus.
    n=32; vertices=[]; faces=[]
    # The crank's outside seat is 45 mm left of the cabinet cheek. These
    # asymmetrical forged layers end immediately before that wood surface.
    for x,r in ((.026,.045),(.032,.058),(.038,.052)):
        for i in range(n):
            a=i*math.tau/n
            wav=.0031*math.sin(7*a+.7)+.0016*math.sin(13*a+1.2)
            radius=r+wav
            vertices.append((x,radius*math.cos(a),radius*math.sin(a)))
    for row in range(2):
        for i in range(n):faces.append((row*n+i,row*n+(i+1)%n,(row+1)*n+(i+1)%n,(row+1)*n+i))
    faces.extend((tuple(reversed(range(n))),tuple(range(2*n,3*n))))
    obj=mesh('Hand cut crank escutcheon',vertices,faces,'ForgedIron')
    for i in (2,8,18,27):
        a=i*math.tau/n
        tube('Escutcheon hand rivet',[(.038,.043*math.cos(a),.043*math.sin(a)),
             (.042,.043*math.cos(a),.043*math.sin(a))],[.0045,.0035],'Brass',7)
    return obj


def merchant_forged_lantern_bracket(cx):
    """A forged upper hook and lower saddle that carry the native lantern."""
    # The triangular web has a real negative-space opening. Its rear flange is
    # seated on the cabinet cheek, and the last eye owns the native lamp hook.
    outer = [(cx-.397, 1.720), (cx-.560, 1.685), (cx-.397, 1.600)]
    inner = [(cx-.420, 1.681), (cx-.512, 1.671), (cx-.420, 1.632)]
    vertices = [(x,y,z) for z in (.075,.081) for loop in (outer,inner)
                for x,y in loop]
    faces=[]
    for layer in (0,6):
        for i in range(3):
            j=(i+1)%3
            face=(layer+i,layer+j,layer+3+j,layer+3+i)
            faces.append(face if layer==0 else tuple(reversed(face)))
    for start in (0,3):
        for i in range(3):
            j=(i+1)%3
            faces.append((start+i,start+j,start+6+j,start+6+i))
    mesh('Lantern pierced forged web',vertices,faces,'ForgedIron',.001)
    # These curved edges and varied rivets follow the web silhouette; they are
    # part of the same non-interactive mount, never independent hit targets.
    for edge in (outer[:2], (outer[1],outer[2])):
        tube('Lantern worn web edge',[(x,y,.071) for x,y in edge],
             [.0048,.0042],'Brass',9)
    plate=[(cx-.386,1.585,.10),(cx-.386,1.730,.10),
           (cx-.386,1.733,.055),(cx-.386,1.584,.055)]
    inset=[(x-.006,y,z) for x,y,z in plate]
    mesh('Lantern shaped mounting flange',plate+inset,
         [(0,1,2,3),(7,6,5,4)]+[(i,(i+1)%4,(i+1)%4+4,i+4) for i in range(4)],
         'ForgedIron',.001)
    for y in (1.602,1.711):
        tube('Lantern flush brass rivet',[(cx-.393,y,.077),(cx-.399,y,.077)],
             [.0055,.0043],'Brass',10)
    ring('Lantern rolled tip eye',(cx-.550,1.677,.078),.018,.006,'ForgedIron',True)
    tube('Lantern short hanging link',[(cx-.550,1.665,.078),(cx-.556,1.634,.078)],
         [.005,.004],'ForgedIron',10)
    ring('Lantern hanging link',(cx-.56,1.62,.078),.026,.005,'Brass',True)
    # A top-only hook made the entire 32 cm native lamp read as a long dangling
    # pendulum in MB571's side view. The actual bottom now sits in this forged
    # saddle at Y=1.27 while the original hook still meets the top eye. The
    # rear riser makes the two bearing points one continuous cabinet fixture.
    tube('Lantern lower saddle arm',[(cx-.394,1.284,.142),
         (cx-.475,1.265,.124),(cx-.560,1.265,.080)],
         [.012,.010,.010],'ForgedIron',12)
    ring('Lantern lower bearing cup',(cx-.560,1.263,.080),.078,.007,
         'ForgedIron')
    tube('Lantern rear load-bearing riser',[(cx-.397,1.602,.155),
         (cx-.492,1.594,.178),(cx-.560,1.485,.184),
         (cx-.560,1.275,.184)], [.008,.008,.007,.007],
         'ForgedIron',10)


def merchant_carved_cheek(cx, side):
    """One continuous, slightly bowed timber cheek in place of stacked boxes."""
    vertices, faces = [], []
    ys = [0.775 + i * (1.65 - .775) / 24 for i in range(25)]
    # The exterior relief is contained within the 0.88 m cabinet envelope.
    for y in ys:
        wave = .006 * math.sin(y * 11.3 + side * .4) + .003 * math.sin(y * 24.7)
        outer = cx + side * (.414 + wave)
        inner = cx + side * (.363 + .002 * math.sin(y * 8.2))
        front = .026 + .004 * math.sin(y * 14.1 + side)
        rear = .532 + .004 * math.sin(y * 9.4 - side)
        for x, z in ((outer, front), (outer, rear), (inner, rear), (inner, front)):
            vertices.append((x, y, z))
    for row in range(24):
        for edge in range(4):
            a = row * 4 + edge
            b = row * 4 + (edge + 1) % 4
            faces.append((a, b, b + 4, a + 4))
    faces += [tuple(reversed(range(4))), tuple(range(96, 100))]
    obj = mesh('Carved one-piece cabinet cheek', vertices, faces, 'DarkWood')
    for poly in obj.data.polygons: poly.use_smooth = len(poly.vertices) == 4
    return obj


def merchant_sculpted_front(name, cx, bottom, top, width, depth, socket_y=None):
    """Continuous hand-cut front board with true surface relief and closed sides."""
    cols, rows = 96, 20
    vertices, faces = [], []
    def front(x, y):
        u = (x - cx) / (width * .5)
        v = (y - bottom) / (top - bottom)
        relief = -.007 * (1 - u*u) * math.sin(v * math.pi)
        relief += .002 * math.sin(17*u + 2*v) * math.sin(v*math.pi)
        if socket_y is not None:
            for index in range(6):
                button_x = -1.25 + index*.12
                radius = math.hypot(x-button_x, y-socket_y)
                relief += .0012*math.exp(-((radius-.049)/.014)**2)
                relief -= .0013*math.exp(-(radius/.030)**2)
        return .027 + relief
    for row in range(rows+1):
        y = bottom + (top-bottom)*row/rows
        for col in range(cols+1):
            u = col/cols
            x = cx + width*(u-.5)
            # Deliberately cut, slightly nonparallel ends, rather than a slab.
            x += .003 * math.sin(y*8.3+u*3.1) * math.sin(u*math.pi)
            vertices.append((x,y,front(x,y)))
    layer=(rows+1)*(cols+1)
    for x,y,z in vertices[:]: vertices.append((x,y,depth))
    for row in range(rows):
        for col in range(cols):
            i=row*(cols+1)+col
            faces.append((i,i+1,i+cols+2,i+cols+1))
            faces.append((i+layer+cols+1,i+layer+cols+2,i+layer+1,i+layer))
    for col in range(cols):
        a=col; b=col+1
        faces.append((a+layer,b+layer,b,a))
        a=rows*(cols+1)+col; b=a+1
        faces.append((a,b,b+layer,a+layer))
    for row in range(rows):
        a=row*(cols+1);b=(row+1)*(cols+1)
        faces.append((a,b,b+layer,a+layer))
        a=row*(cols+1)+cols;b=(row+1)*(cols+1)+cols
        faces.append((a+layer,b+layer,b,a))
    obj=mesh(name,vertices,faces,'DarkWood')
    for poly in obj.data.polygons: poly.use_smooth = True
    return obj


def terrace(name, columns=24, origin=-1.32, curved=True):
    width=columns*.15+.16
    for row in range(8):
        front, back = [], []
        for i in range(33):
            x=(i/32-.5)*width
            bend=.16*(x/1.725)**2 if curved else 0
            front.append((x,origin+row*.13-.063+bend))
            back.append((x,origin+row*.13+.063+bend))
        slab(name+str(row), front+list(reversed(back)), .965+row*.008, .045)
        tube('Stock retaining lip',[(x,.966+row*.008,z-.002) for x,z in front], [.006]*33, 'Brass',6)
    for x in (-width*.44,0,width*.44):
        for z in (origin+.02,origin+.86): turned_leg(x,z,.90)
    # Solid curved cabinet front with panel joinery, recessed fields and carved arches.
    # No drawers: the entire functional stock remains exposed above this apron.
    for i in range(columns//2):
        x=(i-(columns//2-1)/2)*.30
        z=origin-.015+(.16*(x/1.725)**2 if curved else 0)
        rect('Apron joined panel',x,z,.292,.065,.88,.64,bevel=.010)
        for side in (-1,1):
            tube('Panel frame',[(x+side*.128,.26,z-.048),(x+side*.128,.86,z-.048)],[.013]*2)
        for y in (.28,.83):
            tube('Panel frame',[(x-.13,y,z-.048),(x+.13,y,z-.048)],[.013]*2)
        arch=[(x-.095,.40,z-.052),(x-.095,.62,z-.052),(x-.055,.71,z-.052),
              (x,.76,z-.052),(x+.055,.71,z-.052),(x+.095,.62,z-.052),(x+.095,.40,z-.052)]
        tube('Carved pointed arch',arch,[.008]*len(arch))
        for side in (-1,1):
            leaf=[(x+side*.052*math.sin(t*math.pi),.44+t*.19,z-.057) for t in [j/16 for j in range(17)]]
            tube('Carved leaf relief',leaf,[.0045]*17,'Brass',6)
        for y in (.29,.82):
            for side in (-1,1):
                tube('Forged panel rivet',[(x+side*.105,y,z-.054),(x+side*.105,y,z-.060)],[.006,.004],'Brass',8)
    for y,radius in ((.21,.035),(.90,.022)):
        pts=[((i/32-.5)*width,y,origin-.042+(.16*((i/32-.5)*width/1.725)**2 if curved else 0)) for i in range(33)]
        tube('Curved apron moulding',pts,[radius]*33)


def merchant_side_cloth():
    """A touchable crimson travelling banner on the outside left cabinet cheek.

    Station-local, row-major 25x13 mesh for the runtime Cloth solver. Row 0 is
    clamped to an offset crown rail; columns run Z=.205..460. The side wall is
    6 cm behind the fabric so real fingertip contacts have room to displace it.
    The side handle is a separate, rigid fixture in front of the fabric.
    """
    vertices, faces = [], []
    for row in range(25):
        t = row / 24
        for col in range(13):
            u = col / 12
            x = -1.436 - .0025*math.sin(math.pi*t)*math.sin(math.tau*u)
            y = 1.590 - .600*t + .002*math.sin(math.pi*t)*math.cos(3*math.pi*u)
            z = .205 + .255*u + .004*math.sin(math.tau*t)*math.sin(math.pi*u)
            vertices.append((x,y,z))
    for row in range(24):
        for col in range(12):
            i = row*13+col
            faces.append((i,i+13,i+14,i+1)) # outward normal is station -X
    cloth = mesh('ClothRunner_MerchantSide_AltarCloth',vertices,faces,'AltarCloth')
    for polygon in cloth.data.polygons: polygon.use_smooth = True
    bpy.context.view_layer.objects.active = cloth
    modifier = cloth.modifiers.new('Merchant woven cloth thickness','SOLIDIFY')
    modifier.thickness = .003
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    # An actual rod carries the pinned row. Only the fabric deforms on contact.
    tube('Merchant cloth crown rod', [(-1.449,1.593,.198),(-1.449,1.593,.468)],
         [.006,.006], 'ForgedIron', 10)
    for z in (.201,.464):
        tube('Merchant cloth rod ferrule', [(-1.453,1.593,z),(-1.443,1.593,z)],
             [.010,.010], 'Brass', 10)
        tube('Merchant cloth wall standoff', [(-1.356,1.593,z),(-1.449,1.593,z)],
             [.006,.006], 'ForgedIron', 10)
    # The sculpt's integral transport-grip silhouette is covered by the moving
    # fabric. Rebuild it as a rigid, projecting fixture with real screw seats.
    for y in (1.06,1.31):
        tube('Merchant side carry eye', [(-1.355,y,.270),(-1.480,y,.270)],
             [.008,.011], 'ForgedIron', 12)
        tube('Merchant side cloth grommet',
             [(-1.444,y+.014*math.cos(i*math.tau/32),
               .270+.014*math.sin(i*math.tau/32)) for i in range(33)],
             [.003]*33,'Brass',6)
    tube('Merchant side leather grip', [(-1.477,1.06,.270),(-1.500,1.10,.270),
         (-1.500,1.27,.270),(-1.477,1.31,.270)],
         [.012,.018,.018,.012], 'Leather', 14)


def merchant():
    # Approved build-549 concept: a chest-height travelling cabinet BESIDE the actor,
    # with a separate folding ledger stand. Metre-space contract is shared with runtime.
    cx = -.95
    for side in (-1, 1):
        x = cx + side*.405
        # Fold-out legs and positive stops explain how a portable cabinet stands up.
        for z, foot in ((.15,-.025),(.45,.64)):
            def leg_z(t):
                # The old (z + foot) * t only happened to work for the front foot.
                # For the rear foot it overshot the hinge by 32 cm at mid-height,
                # then doubled back into the sharp, disconnected zigzag in MB571.
                return foot + (z-foot)*t
            # A single hand-hewn, bowed timber per leg carries the cabinet;
            # uneven section thickness and knots keep it from reading as a
            # smooth cylindrical prototype at normal VR inspection distance.
            tube('Cabinet folding trestle',[
                 (cx+side*.46,.006,foot),
                 (cx+side*.455,.085,leg_z(.06)),
                 (cx+side*.446,.19,leg_z(.22)),
                 (cx+side*.42,.34,leg_z(.40)),
                 (cx+side*.396,.52,leg_z(.65)),
                 (cx+side*.374,.68,leg_z(.84)),
                 (cx+side*.35,.86,z)],
                 [.034,.037,.033,.035,.032,.034,.038],sides=14)
            tube('Iron foot shoe',[(cx+side*.46,.007,foot),
                 (cx+side*.453,.11,leg_z(.07))],[.039,.042],'ForgedIron',12)
        # Two forged diagonal stays form an X between the *two* legs of each
        # trestle. Both ends enter timber at the sampled leg centreline; a brace
        # drawn along one leg looked like a loose rod from the side and under it.
        front_low = (-.025 + (.15+.025)*.34, .31)
        front_high = (-.025 + (.15+.025)*.79, .63)
        rear_low = (.64 + (.45-.64)*.34, .31)
        rear_high = (.64 + (.45-.64)*.79, .63)
        for start, end, offset in ((front_low,rear_high,-.013),(rear_low,front_high,.013)):
            tube('Trestle diagonal iron stay',
                 [(cx+side*(.427+offset),start[1],start[0]),
                  (cx+side*(.381+offset),end[1],end[0])],
                 [.010,.010],'ForgedIron',10)
        ring('Folding pivot washer',(x,.825,.090),.030,.006,'ForgedIron',True)
        for y in (.80,1.605):
            rect('Corner band vertical',x,.010,.070,.010,y+.025,.12,'ForgedIron',.004)
            rect('Corner band horizontal',x-side*.050,.010,.14,.010,y+.025,.026,'ForgedIron',.004)
            for dx,dy in ((0,0),(0,-.066),(-side*.095,.01)):
                tube('Hammered corner rivet',[(x+dx,y+dy,.002),(x+dx,y+dy,-.009)],
                     [.009,.006],'Brass',10)
        # Thick stitched leather side handles, visibly bolted to the transport chest.
        outer=x+side*.025
        for y in (1.06,1.31):
            tube('Carry handle eye',[(outer,y,.27),(outer+side*.035,y,.27)],
                 [.012,.012],'ForgedIron',12)
        tube('Leather carrying grip',[(outer,1.06,.27),(outer+side*.055,1.10,.27),
             (outer+side*.055,1.27,.27),(outer,1.31,.27)], [.012,.019,.019,.012],'Leather',14)
        # The cassette moves along actual bounded rails inside the box.
        for y in (1.02,1.42):
            tube('Cassette guide rail',[(cx+side*.370,y,.072),(cx+side*.370,y,.485)],
                 [.007,.007],'ForgedIron',8)
    for i in range(6):
        rect('Rear vertical timber',cx+(i-2.5)*.133,.508,.131,.026,1.63,.86,bevel=.004)
    # Real, relief-cut front boards retain exact native cassette/button seats.
    # The fal surface contributes worn side/crown detail but never a frozen card.
    # The bisected provider sculpt has an open, ragged underside. A full timber
    # sole closes that cut so the player sees a sound base from below, while the
    # four separate trestles still pass through into the carcass above it.
    rect('Cabinet underside plank',cx,.25,.83,.42,.785,.025,bevel=.006)
    rect('Cabinet crown',cx,.29,.87,.49,1.665,.07,bevel=.015)
    rect('Cabinet sill',cx,.30,.85,.45,.785,.046,bevel=.012)
    merchant_sculpted_front('Carved category fascia',cx,.772,.968,.83,.097,.840)
    # Header leaves enough hidden roof depth for the bifold opaque changeover shutter.
    merchant_sculpted_front('Carved cabinet header',cx,1.511,1.613,.85,.085)
    for y in (.792,.954,1.535):
        tube('Beaded front moulding',[(cx-.377,y,.012),(cx,y-.003,.009),(cx+.377,y,.012)],
             [.009,.009,.009],sides=12)
    # Top carrying handle and a forged bracket for the original game's lantern.
    for x in (cx-.10,cx+.10):
        tube('Top handle mount',[(x,1.665,.31),(x,1.716,.31)],[.009,.009],'ForgedIron',10)
    tube('Top leather carry bar',[(cx-.10,1.716,.31),(cx+.10,1.716,.31)], [.016,.016],'Leather',14)
    merchant_forged_lantern_bracket(cx)
    merchant_side_cloth()
    # Small folding writing stand, with dovetail-like board ends and iron X braces.
    for i in range(5):
        # The merchant's coat reaches Z=.368 at table height across the work cycle.
        # A stepped rear contour preserves both side contact ledges without cutting into it.
        rear = .405 if i in (0,4) else .310
        rect('Ledger worktop board',(i-2)*.144,(rear-.05)/2,.142,rear+.05,.955,.046,bevel=.009)
    rect('Ledger leather writing pad',0,.125,.60,.35,.958,.004,'Leather',.002)
    for side in (-1,1):
        for a,b in ((-.05,.39),(.39,-.05)):
            tube('Ledger folding support',[(side*.33,.006,a),(side*.27,.48,.205),(side*.30,.921,b)],
                 [.027,.024,.028],sides=12)
        tube('Ledger pivot bolt',[(side*.255,.48,.205),(side*.295,.48,.205)], [.017,.017],'Brass',12)
        tube('Ledger cross stay',[(side*.285,.30,.13),(side*.285,.76,.30)], [.008,.008],'ForgedIron',8)
    tube('Ledger rear stretcher',[(-.29,.13,.35),(.29,.13,.35)],[.019,.019],sides=12)
    for x in (-.325,.325):
        for z in (-.035,.38):
            tube('Ledger countersunk pin',[(x,.955,z),(x,.958,z)],[.006,.006],'Brass',10)


def merchant_cassette():
    # Three articulated shelves retain their original four seats and brass clips.
    # The runtime rolls these real holders around the cabinet lips with their cards.
    for x in (-.35,.35):
        rect('Cassette timber side',x,.006,.015,.035,.25,.51,bevel=.003)
    for row in range(3):
        y=(row-1)*.17
        start=len(parts)
        rect('Articulated shelf backing',0,.026,.678,.022,y+.069,.138,bevel=.004)
        for col in range(4):
            x=(col-1.5)*.18
            for dx in (-.063,.063):
                tube('Card brass retaining clip',[(x+dx,y-.059,.012),(x+dx,y-.061,-.011),
                     (x+dx,y-.040,-.014)], [.0035]*3,'Brass',8)
                tube('Card clip rivet',[(x+dx,y-.060,.013),(x+dx,y-.060,-.012)],[.005,.005],'ForgedIron',8)
            rect('Individual card leather seat',x,.010,.144,.006,y+.057,.114,'Leather',.002)
        hinge=bpy.data.objects.new(f'Row{row}',None)
        bpy.context.collection.objects.link(hinge)
        hinge.location=xyz((0,y,0))
        for obj in parts[start:]:
            obj.parent=hinge
            obj.location=xyz((0,-y,0))


def merchant_crank():
    # Rotation axis +X. The rear spindle seats on the right cabinet cheek at
    # X=-.525 in station coordinates, while the grip's full radius remains left
    # of the ledger edge at X=-.356. Named Handle retains its exact grip collider.
    worn_crank_plate()
    tube('Crank axle',[(.045,0,0),(.060,.001,-.002),(.080,-.001,0)],
         [.014,.018,.016],'ForgedIron',11)
    forged_crank_arm()
    hand_carved_crank_grip()
    for x in (.105,.155):
        tube('Worn grip ferrule',[(x,-.12,-.030),(x+.006,-.121,-.030)],
             [.021,.022],'Brass',11)


def merchant_button():
    hand_turned_disc('Button turned walnut body',[(.014,.032),(.010,.041),
        (.004,.047),(-.006,.048),(-.010,.044),(-.013,.039)],'DarkWood')
    hand_turned_disc('Button hammered brass bezel',[( -.010,.046),(-.013,.049),
        (-.017,.047),(-.019,.040),(-.019,.033),(-.016,.031)],'Brass',False)
    hand_turned_disc('Button carved inner face',[( -.015,.032),(-.016,.031),
        (-.018,.027),(-.018,.001)],'DarkWood')
    points=[(.0318*math.cos(i*math.tau/48),.0318*math.sin(i*math.tau/48),-.020)
            for i in range(49)]
    tube('Button inset forged border',points,[.00135]*len(points),'ForgedIron',8)
    for index in range(12):
        angle=math.tau*index/12
        x=.0425*math.cos(angle);y=.0425*math.sin(angle)
        tube('Button peened bezel rivet',[(x,y,-.019),(x,y,-.022)],
             [.0022,.00165],'Brass',8)
    # Original native category pictogram is runtime artwork on the front, never invented card art.


def merchant_shutter_leaf():
    for i in range(3):
        rect('Shutter tongue and groove',0,.006,.72,.016,-i*.088,.087,bevel=.003)
    for x in (-.30,.30):
        rect('Shutter iron hinge strap',x,-.004,.022,.007,-.004,.253,'ForgedIron',.002)
        for y in (-.015,-.245):
            tube('Shutter hinge pin',[(x,y,-.003),(x,y,-.009)],[.005,.004],'Brass',8)


def merchant_return():
    # Legacy template address remains loadable, but no inventory-dependent returns
    # are instantiated. Retain a small folded travel case rather than the huge terrace.
    rect('Folded carrying case',0,0,.68,.18,.30,.30,bevel=.022)
    for x in (-.22,.22): rect('Case strap',x,-.095,.022,.009,.29,.27,'Brass',.004)


def enchantress():
    outline=[]
    for i in range(80):
        a=i*math.tau/80
        r=1+.025*math.sin(a*7)+.015*math.sin(a*13)
        outline.append((.86*r*math.cos(a),.03+.46*r*math.sin(a)))
    slab('Live edge rootwood slab',outline,.955,.09,bevel=.012)
    for x in (-.62,.62):
        for z in (-.24,.28):
            for strand in range(3):
                pts=[(x+.045*math.cos(t*5+strand*2),t*.91,z+.035*math.sin(t*5+strand*2)) for t in [j/24 for j in range(25)]]
                tube('Twisted root support',pts,[.028+.012*math.sin(i/24*math.pi) for i in range(25)])
            for y in (.12,.74): ring('Root collar',(x,y,z),.071,.008)
    tube('Bent root stretcher',[(-.64,.26,0),(-.3,.22,.04),(0,.30,0),(.3,.24,-.03),(.64,.26,0)],[.035]*5)
    outline=[(.27*math.cos(i*math.tau/64),-.05+.27*math.sin(i*math.tau/64)) for i in range(64)]
    slab('Engraved slate inset',outline,.958,.012,'PaleStone',.004)
    for radius in (.225,.253): ring('Incised brass circle',(0,.960,-.05),radius,.0025)
    for i in range(6):
        a=i*math.tau/6
        tube('Hexagram inlay',[(.21*math.cos(a),.961,-.05+.21*math.sin(a)),(.21*math.cos(a+math.tau/3),.961,-.05+.21*math.sin(a+math.tau/3))],[.0018]*2,'Brass',5)
    drape(-.60,.22,'enchantress')
    # Supported native lantern perch; handoff at (-.18,1.12,.18) stays clear.
    rect('Lamp return',.70,.63,.32,.64,.955,.06)
    turned_leg(.70,.83)


def priestess():
    outline=[(.81*math.cos(i*math.tau/64),.38*math.sin(i*math.tau/64)) for i in range(64)]
    slab('Scalloped stone mensa',outline,.955,.11,'PaleStone',.018)
    for x in (-.51,.51):
        rect('Dressed stone plinth',x,0,.32,.49,.11,.11,'PaleStone',.016)
        tube('Tapered stone pier',[(x,.09,0),(x,.24,0),(x,.60,0),(x,.84,0)],[.14,.095,.10,.16],'PaleStone',8)
        for y in (.22,.64): ring('Pier binding',(x,y,0),.111,.007)
    # A pointed open arch: the negative space is modelled, never painted onto a cube.
    pts=[(-.48,.38,0),(-.42,.53,0),(-.28,.65,0),(0,.80,0),(.28,.65,0),(.42,.53,0),(.48,.38,0)]
    tube('Gothic stone arch',pts,[.055]*len(pts),'PaleStone',12)
    for x in (-.58,.58): drape(x,.23,'priestess')
    for i in range(13):
        x=(i-6)*.108; z=-.36*math.sqrt(max(0,1-(x/.81)**2))
        pts=[(x-.035,.872,z-.006),(x-.025,.901,z-.01),(x,.923,z-.012),(x+.025,.901,z-.01),(x+.035,.872,z-.006)]
        tube('Mensa carved arch',pts,[.005]*5,'PaleStone',6)


def export(name, build):
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    parts.clear(); build()
    if name == 'merchant':
        # The multiview, UV-atlased carcass replaces the entire former box/rail
        # prototype, not just its side cheeks. Keep only supports, live-cassette
        # guides, the native lantern's physical hook and the separate ledger.
        retained = ('Cabinet folding trestle', 'Trestle diagonal iron stay',
                    'Iron foot shoe', 'Lantern ', 'ClothRunner_MerchantSide_',
                    'Merchant cloth ', 'Merchant side ', 'Cabinet underside plank',
                    'Ledger ')
        for obj in tuple(parts):
            if not obj.name.startswith(retained):
                parts.remove(obj)
                bpy.data.objects.remove(obj, do_unlink=True)
    # Keep only physical floor-contacting supports separate: their ordinary transforms
    # stretch down to terrain while their authored tops stay fixed. This presentation is
    # mirrored by the existing multiplayer node transforms, without private mesh updates.
    supports=[]
    for obj in parts:
        heights=[(obj.matrix_world @ vertex.co).z for vertex in obj.data.vertices]
        if name in ('merchant','enchantress','priestess') and heights and min(heights) <= .03 and max(heights)-min(heights) >= .06:
            obj.name=f'GroundSupport{len(supports):02d}_{obj.name}_{obj.data.materials[0].name}'
            supports.append(obj)
    # The remaining decor stays one renderer per material, not one per rivet or leaf.
    parents={obj.parent for obj in parts}
    for parent in parents:
        for material in materials.values():
            objects=[o for o in bpy.context.scene.objects if o.type == 'MESH' and o not in supports
                     and o.parent == parent and not o.name.startswith(('Handle_','ClothRunner_','ClothDecoration_')) and o.data.materials[0] == material]
            if not objects: continue
            bpy.ops.object.select_all(action='DESELECT')
            for obj in objects: obj.select_set(True)
            bpy.context.view_layer.objects.active=objects[0]
            bpy.ops.object.join()
            objects[0].name=name+('_'+parent.name if parent else '')+'_'+material.name
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=str(out/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},
        apply_unit_scale=True,axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,
        use_mesh_modifiers=True,mesh_smooth_type='FACE')
    bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'.blend')))
    print('TOWN_FURNITURE',name,sum(len(o.data.polygons) for o in bpy.context.scene.objects if o.type=='MESH'))


for name, build in [('merchant',merchant),('enchantress',enchantress),('priestess',priestess),
                    ('merchant_return',merchant_return),('merchant_cassette',merchant_cassette),
                    ('merchant_crank',merchant_crank),('merchant_button',merchant_button),
                    ('merchant_shutter_leaf',merchant_shutter_leaf)]:
    if not args.only or name in args.only:
        export(name,build)
