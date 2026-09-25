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


def turned_leg(x, z, height=.9, stone=False):
    profile = [(0,.10),(.035,.11),(.075,.09),(.12,.055),(.22,.06),
               (.28,.065),(.34,.04),(.57,.042),(.70,.07),(.77,.08),(.84,.065),(.90,.09)]
    points = [(x+.025*math.sin(y*7), y*height/.9, z+.015*math.sin(y*9)) for y,r in profile]
    tube('Hand turned support', points, [r for y,r in profile], 'PaleStone' if stone else 'DarkWood', 16)
    for y in (.12,.73):
        ring('Forged collar', (x,y,z), .061, .009)


def drape(x, width, z, top=.964, material='AltarCloth'):
    # The former vertical fold began over the stone. Its vertices entered the
    # mensa below the top face; a static renderer hid the fault until close VR
    # inspection. Cross the measured front edge at tabletop height first. The
    # free edge is a separate mesh so runtime cloth motion cannot drag the
    # stone or detach a decorative stitched border.
    vertices, faces = [], []
    for row in range(25):
        t = row/24
        for col in range(13):
            u = col/12
            xx = x+(u-.5)*width
            fold = .007*math.sin(u*math.tau*3.2+.3)
            zz = .12+(-.50)*min(1,t/.45) - .012*math.sin(u*math.tau*3)*(max(0,t-.45)/.55)
            yy = top+.007+fold - .50*max(0,(t-.45)/.55)
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
    decorations.append(ring('Runner sun embroidery',(x,top-.31,-.400),width*.19,.0025,vertical=True))
    for sign in (-1,1):
        points=[vertices[row*13+(1 if sign < 0 else 11)] for row in range(25)]
        decorations.append(tube('Runner stitched border',[(a,b+.002,c-.004) for a,b,c in points],
                                [.0016]*25,'Brass',5))
    for i in range(12):
        a=i*math.tau/12
        decorations.append(tube('Embroidered ray',[(x+width*.22*math.cos(a),top-.31+width*.22*math.sin(a),-.400),
             (x+width*.28*math.cos(a),top-.31+width*.28*math.sin(a),-.400)],[.0018]*2,'Brass',5))
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
    for x,r in ((-.021,.045),(-.015,.058),(-.010,.052)):
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
        tube('Escutcheon hand rivet',[(-.007,.043*math.cos(a),.043*math.sin(a)),
             (-.003,.043*math.cos(a),.043*math.sin(a))],[.0045,.0035],'Brass',7)
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


def merchant():
    # Approved build-549 concept: a chest-height travelling cabinet BESIDE the actor,
    # with a separate folding ledger stand. Metre-space contract is shared with runtime.
    cx = -.95
    for side in (-1, 1):
        x = cx + side*.405
        rect('Cabinet solid cheek',x,.30,.04,.45,1.64,.88,bevel=.011)
        rect('Front worn stile',x,.049,.070,.060,1.65,.88,bevel=.011)
        rect('Rear corner post',x,.525,.055,.042,1.65,.89,bevel=.009)
        # Fold-out legs and positive stops explain how a portable cabinet stands up.
        for z, foot in ((.15,-.025),(.45,.64)):
            tube('Cabinet folding trestle',[(cx+side*.46,.006,foot),
                 (cx+side*.42,.34,(z+foot)*.5),(cx+side*.35,.86,z)],
                 [.031,.030,.035],sides=12)
            tube('Trestle iron stay',[(cx+side*.415,.30,(z+foot)*.5),
                 (cx+side*.375,.65,z+.015)],[.009,.009],'ForgedIron',8)
            tube('Iron foot shoe',[(cx+side*.46,.007,foot),
                 (cx+side*.455,.078,foot+.008)],[.037,.035],'ForgedIron',12)
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
    rect('Cabinet crown',cx,.29,.87,.49,1.665,.07,bevel=.015)
    rect('Cabinet sill',cx,.30,.85,.45,.785,.046,bevel=.012)
    rect('Control fascia',cx,.064,.83,.066,.968,.196,bevel=.012)
    # Header leaves enough hidden roof depth for the bifold opaque changeover shutter.
    rect('Front header',cx,.052,.85,.060,1.613,.102,bevel=.010)
    for y in (.792,.954,1.535):
        tube('Beaded front moulding',[(cx-.377,y,.012),(cx,y-.003,.009),(cx+.377,y,.012)],
             [.009,.009,.009],sides=12)
    # Top carrying handle and a forged bracket for the original game's lantern.
    for x in (cx-.10,cx+.10):
        tube('Top handle mount',[(x,1.665,.31),(x,1.716,.31)],[.009,.009],'ForgedIron',10)
    tube('Top leather carry bar',[(cx-.10,1.716,.31),(cx+.10,1.716,.31)], [.016,.016],'Leather',14)
    tube('Lantern bracket',[(cx-.39,1.60,.30),(cx-.39,1.72,.30),
         (cx-.46,1.74,.19),(cx-.56,1.71,.08),(cx-.56,1.65,.08)],
         [.010,.010,.010,.009,.008],'ForgedIron',12)
    ring('Lantern hanging link',(cx-.56,1.62,.08),.026,.004,'Brass',True)
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
    # Rotation axis +X. Named Handle geometry remains separate for the grip collider.
    worn_crank_plate()
    tube('Crank axle',[(-.055,0,0),(-.03,.001,-.002),(.022,-.001,0)],
         [.014,.018,.016],'ForgedIron',11)
    tube('Hammered bent crank arm',[(.021,0,0),(.029,-.020,-.004),(.037,-.046,-.014),
         (.033,-.080,-.023),(.039,-.118,-.031)],
         [.017,.015,.014,.012,.015],'ForgedIron',9)
    tube('Handle_DarkWood',[(.040,-.119,-.031),(.057,-.120,-.031),(.082,-.121,-.030),
         (.113,-.119,-.033),(.137,-.121,-.029),(.153,-.118,-.032)],
         [.019,.023,.026,.025,.022,.017],'DarkWood',13)
    for x in (.048,.138):
        tube('Worn grip ferrule',[(x,-.12,-.030),(x+.006,-.121,-.030)],
             [.021,.022],'Brass',11)


def merchant_button():
    tube('Button turned wood',[(0,0,.006),(0,0,-.011)],[.046,.043],'DarkWood',32)
    tube('Button brass inset',[(0,0,-.012),(0,0,-.017)],[.037,.036],'Brass',32)
    ring('Button milled rim',(0,0,-.011),.043,.003,'Brass',True)
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
    drape(-.60,.22,-.32)
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
    for x in (-.58,.58): drape(x,.23,-.285)
    for i in range(13):
        x=(i-6)*.108; z=-.36*math.sqrt(max(0,1-(x/.81)**2))
        pts=[(x-.035,.872,z-.006),(x-.025,.901,z-.01),(x,.923,z-.012),(x+.025,.901,z-.01),(x+.035,.872,z-.006)]
        tube('Mensa carved arch',pts,[.005]*5,'PaleStone',6)


def export(name, build):
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    parts.clear(); build()
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
