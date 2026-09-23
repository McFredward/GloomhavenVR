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
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
out = Path(args.output).resolve()
out.mkdir(parents=True, exist_ok=True)
parts = []
materials = {}
for name, color, metal in [('DarkWood', (.20, .09, .035, 1), 0),
                           ('PaleStone', (.38, .34, .27, 1), 0),
                           ('Brass', (.35, .21, .07, 1), .7),
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
    return (p[0], -p[2], p[1])


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
    tube(name, pts, [wire]*49, material, 6)


def turned_leg(x, z, height=.9, stone=False):
    profile = [(0,.10),(.035,.11),(.075,.09),(.12,.055),(.22,.06),
               (.28,.065),(.34,.04),(.57,.042),(.70,.07),(.77,.08),(.84,.065),(.90,.09)]
    points = [(x+.025*math.sin(y*7), y*height/.9, z+.015*math.sin(y*9)) for y,r in profile]
    tube('Hand turned support', points, [r for y,r in profile], 'PaleStone' if stone else 'DarkWood', 16)
    for y in (.12,.73):
        ring('Forged collar', (x,y,z), .061, .009)


def drape(x, width, z, top=.964, material='AltarCloth'):
    vertices, faces = [], []
    for row in range(25):
        t = row/24
        for col in range(13):
            u = col/12
            xx = x+(u-.5)*width
            zz = z+.56*(1-min(1,t*2))-.014*math.sin(u*math.tau*3)*(max(0,t-.5)*2)
            yy = top+.006*math.sin(u*math.tau*3)-max(0,t-.5)*1.1
            yy -= .065*max(0,(t-.85)/.15)*(1-abs(u*2-1))
            vertices.append((xx,yy,zz))
    for row in range(24):
        for col in range(12):
            i=row*13+col
            faces.append((i,i+1,i+14,i+13))
    obj=mesh('Embroidered draped runner', vertices, faces, material)
    for poly in obj.data.polygons: poly.use_smooth = True
    bpy.context.view_layer.objects.active=obj
    mod=obj.modifiers.new('Woven cloth thickness','SOLIDIFY'); mod.thickness=.003
    bpy.ops.object.modifier_apply(modifier=mod.name)
    # The front embroidered medallion is real relief and remains readable in stereo.
    ring('Runner sun embroidery',(x,top-.31,z-.008),width*.21,.003,vertical=True)
    for sign in (-1,1):
        points=[vertices[row*13+(1 if sign < 0 else 11)] for row in range(25)]
        tube('Runner stitched border',[(a,b+.002,c-.002) for a,b,c in points],[.0018]*25,'Brass',5)
    for i in range(12):
        a=i*math.tau/12
        tube('Embroidered ray',[(x+width*.24*math.cos(a),top-.31+width*.24*math.sin(a),z-.009),
             (x+width*.30*math.cos(a),top-.31+width*.30*math.sin(a),z-.009)],[.002]*2,'Brass',5)


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
    # A portable folding campaign cabinet, not a dining hall. Its two revolving
    # front trays are runtime parts; fixed furniture never grows with inventory.
    for x in (-.65,.65):
        turned_leg(x,.16)
        # Fold-out front legs have visible brass hinge pivots and curved braces.
        tube('Folding front leg',[(x,0,-.69),(x,.38,-.61),(x,.72,-.49)],[.036,.029,.04])
        ring('Front leg hinge',(x,.71,-.49),.049,.009)
        tube('Folding diagonal brace',[(x,.24,-.62),(x,.50,-.34),(x,.83,.02)],[.014]*3,'Brass',8)
    # Clear native coin/ledger contact patch remains at the original height.
    for i in range(6): rect('Ledger worktop plank',(i-2.5)*.232,.16,.230,.64,.955,.055)
    rect('Ledger leather',0,.15,.72,.40,.958,.004,'Leather',.002)
    for x in (-.70,.70):
        rect('Rounded cabinet cheek',x,-.14,.055,.69,.91,.47,bevel=.020)
        tube('Forged carry handle',[(x,.56,-.28),(x*1.04,.54,-.23),(x*1.04,.54,-.08),(x,.56,-.03)],[.009]*4,'Brass',10)
    # Lower travelling trunk; revolving racks occupy x ±.35,z -.57,y .77.
    rect('Travel trunk floor',0,-.40,1.36,.58,.43,.06,bevel=.017)
    rect('Travel trunk apron',0,-.686,1.36,.035,.44,.19,bevel=.014)
    for x in (-.64,0,.64):
        rect('Forged travel strap',x,-.708,.027,.008,.44,.19,'Brass',.004)
    for x in (-.35,.35):
        # Top/bottom bearings support each visible turning card tray.
        tube('Rack bearing upright',[(x,.43,-.57),(x,.455,-.57)],[.018]*2,'Brass',10)
        tube('Rack upper pin',[(x,1.115,-.57),(x,1.15,-.57)],[.014]*2,'Brass',10)
    for x in (-.685,.685):
        tube('Rack folding frame',[(x,.43,-.57),(x,1.12,-.57)],[.020]*2)
    tube('Folding cabinet crown',[(-.685,1.12,-.57),(0,1.15,-.57),(.685,1.12,-.57)],[.022]*3)
    for x in (-.35,.35):
        for z in (-.70,-.13):
            tube('Leather securing belt',[(x,.443,z),(x,.451,z+.10)],[.012]*2,'Leather',6)


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
        if heights and min(heights) <= .03 and max(heights)-min(heights) >= .06:
            obj.name=f'GroundSupport{len(supports):02d}_{obj.name}_{obj.data.materials[0].name}'
            supports.append(obj)
    # The remaining decor stays one renderer per material, not one per rivet or leaf.
    for material in materials.values():
        objects=[o for o in bpy.context.scene.objects if o.type == 'MESH' and o not in supports and o.data.materials[0] == material]
        if not objects: continue
        bpy.ops.object.select_all(action='DESELECT')
        for obj in objects: obj.select_set(True)
        bpy.context.view_layer.objects.active=objects[0]
        bpy.ops.object.join()
        objects[0].name=name+'_'+material.name
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(filepath=str(out/(name+'.fbx')),use_selection=True,object_types={'MESH'},
        apply_unit_scale=True,axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False,
        use_mesh_modifiers=True,mesh_smooth_type='FACE')
    bpy.ops.wm.save_as_mainfile(filepath=str(out/(name+'.blend')))
    print('TOWN_FURNITURE',name,sum(len(o.data.polygons) for o in bpy.context.scene.objects if o.type=='MESH'))


export('merchant',merchant)
export('enchantress',enchantress)
export('priestess',priestess)
export('merchant_return',merchant_return)
