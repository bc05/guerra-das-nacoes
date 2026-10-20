"""Constraint-free gait and anatomical correction of the existing 17-bone rig."""
import math
import bpy
from mathutils import Matrix, Vector

GAITS = {
    'Walk': dict(last=31, support=.62, distance=1.00, lift=.065, tangent_width=.24),
    'Run': dict(last=23, support=.26, distance=2.20, lift=.18, tangent_width=.18),
}


def set_frame(scene, frame):
    scene.frame_set(math.floor(frame), subframe=frame-math.floor(frame))


def smootherstep(value):
    value = min(1, max(0, value))
    return value**3*(10-15*value+6*value*value)


def swing_tangent(value, width):
    """Short quintic Hermite sections preserve takeoff/landing velocity and acceleration.

    A full-cycle Hermite overshoots behind the hip; local sections let the
    foot recover promptly without the old power-8 acceleration discontinuity.
    """
    def lobe(t):
        return t-6*t**3+8*t**4-3*t**5
    if value < width:
        return width*lobe(value/width)
    if value > 1-width:
        return -width*lobe((1-value)/width)
    return 0


def sole_contact(sole, angle):
    rotation = Matrix.Rotation(angle,4,'X')
    lowest = min((rotation @ point).z for point in sole)
    pivot_y = max(point.y for point in sole) if angle < 0 else min(point.y for point in sole)
    pivot = Vector((0,pivot_y,-.14))
    return pivot.y-(rotation @ pivot).y, -.14-lowest+.002


def foot_target(phase, config, sole, side):
    support, distance = config['support'], config['distance']
    amplitude = distance*support*.5
    u = phase % 1
    if u < support:
        progress = u/support
        y = -amplitude+distance*u
        angle = -.12*smootherstep(1-progress/.18)+.22*smootherstep((progress-.72)/.28)
        dy,dz = sole_contact(sole,angle)
        height = 0
    else:
        swing = (u-support)/(1-support)
        eased = smootherstep(swing)
        y = amplitude*(1-2*eased)+distance*(1-support)*swing_tangent(swing,config['tangent_width'])
        height = config['lift']*math.sin(math.pi*swing)**4
        angle = .22*(1-eased)-.12*eased-.08*math.sin(math.pi*swing)**4
        # Interpolate the contact offsets in the air: reselecting the lowest
        # sole vertex at zero pitch made the old swing trajectory kink.
        rear_y,rear_z = sole_contact(sole,.22)
        front_y,front_z = sole_contact(sole,-.12)
        dy = rear_y*(1-eased)+front_y*eased
        dz = rear_z*(1-eased)+front_z*eased
    return Vector((side*.145,y+dy,.14+height+dz)), Matrix.Rotation(angle,4,'X')


def periodic_tangents(rig):
    """Keep both pose and velocity continuous at the exported cycle boundary."""
    for clip in bpy.data.actions:
        if clip.name not in ('Idle','Walk','Run'): continue
        for layer in clip.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for curve in bag.fcurves:
                        keys = list(curve.keyframe_points)
                        if len(keys) < 3: continue
                        duration = keys[-1].co.x-keys[0].co.x
                        drift = keys[-1].co.y-keys[0].co.y
                        for i,point in enumerate(keys):
                            before = keys[i-1].co.copy() if i else keys[-2].co-Vector((duration,drift))
                            after = keys[i+1].co.copy() if i+1 < len(keys) else keys[1].co+Vector((duration,drift))
                            slope = (after.y-before.y)/(after.x-before.x)
                            left = (point.co.x-before.x)/3
                            right = (after.x-point.co.x)/3
                            point.interpolation = 'BEZIER'
                            point.handle_left_type = point.handle_right_type = 'FREE'
                            point.handle_left = point.co-Vector((left,slope*left))
                            point.handle_right = point.co+Vector((right,slope*right))
                        curve.update()


def reset(rig):
    for bone in rig.pose.bones:
        bone.rotation_mode = 'XYZ'
        bone.matrix_basis.identity()
    bpy.context.view_layer.update()


def key(rig, frame):
    for bone in rig.pose.bones:
        bone.keyframe_insert(data_path='rotation_euler', frame=frame, group=bone.name)
        bone.keyframe_insert(data_path='location', frame=frame, group=bone.name)


def action(rig, name):
    old = bpy.data.actions.get(name)
    if old is not None:
        if rig.animation_data.action == old:
            rig.animation_data.action = None
        bpy.data.actions.remove(old)
    clip = bpy.data.actions.new(name)
    clip.use_fake_user = True
    rig.animation_data.action = clip
    return clip


def orient(rig, name, start, end=None, rotation=None):
    rest = rig.data.bones[name]
    if rotation is None:
        rotation = (rest.tail_local-rest.head_local).normalized().rotation_difference((end-start).normalized()).to_matrix().to_4x4()
    bone = rig.pose.bones[name]
    bone.matrix = Matrix.Translation(start) @ rotation @ rest.matrix_local.to_3x3().to_4x4()
    bone.location = (0, 0, 0)
    bone.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def solve_limb(rig, upper_name, lower_name, end_name, target, pole):
    upper, lower = rig.data.bones[upper_name], rig.data.bones[lower_name]
    first, second = (upper.tail_local-upper.head_local).length, (lower.tail_local-lower.head_local).length
    origin = rig.pose.bones[upper_name].head.copy()
    delta = target-origin
    distance = delta.length
    assert abs(first-second)+.0001 < distance < first+second-.0001, (upper_name, bpy.context.scene.frame_current, distance, first+second, tuple(origin), tuple(target))
    axis = delta.normalized()
    projection = (first*first-second*second+distance*distance)/(2*distance)
    altitude = math.sqrt(max(0, first*first-projection*projection))
    bend = (pole-axis*pole.dot(axis)).normalized()
    elbow = origin+axis*projection+bend*altitude
    orient(rig, upper_name, origin, elbow)
    orient(rig, lower_name, elbow, target)
    return rig.pose.bones[end_name].head.copy()


def carry_guard(rig, phase=0, running=False, breathing=0):
    """Separate sword/shield guards, with wrists following their own forearms."""
    for suffix, side in [('R', 1), ('L', -1)]:
        beat = math.sin(phase*math.tau) * side
        if suffix == 'R':
            target = Vector((.43, -.20, 1.06 if running else 1.03))
            target += Vector((0, .025*beat, .012*beat + breathing))
        else:
            target = Vector((-.36, -.27, 1.20 if running else 1.18))
            target += Vector((0, .012*beat, .008*beat + breathing))
        wrist = solve_limb(rig, 'UpperArm.'+suffix, 'Forearm.'+suffix,
                           'Hand.'+suffix, target, Vector((side*.32, .35, -.18)))
        forearm = rig.pose.bones['Forearm.'+suffix]
        rest = rig.data.bones['Forearm.'+suffix]
        rotation = forearm.matrix.to_3x3() @ rest.matrix_local.to_3x3().inverted()
        # A small wrist extension preserves the palm/forearm line and a low blade guard.
        if suffix == 'R':
            rotation = Matrix.Rotation(.20, 3, 'X') @ rotation
        orient(rig, 'Hand.'+suffix, wrist, rotation=rotation.to_4x4())


def refine_locomotion(rig, body):
    """Keep attack path/impact; bake IK with fixed support-foot velocity.

    Walk: 1s, 1m/cycle, 62% support per foot (double support).
    Run: .733s, 2.2m/cycle, 26% support per foot (short flight phases).
    All IK is authoring-only. No constraints or runtime solvers are exported.
    """
    scene = bpy.context.scene
    # Use the actual boot soles, not an assumed ankle height, for the rocker contact.
    soles = {}
    for suffix in ('R', 'L'):
        ankle = rig.data.bones['Foot.'+suffix].head_local
        soles[suffix] = [v.co-ankle for v in body.data.vertices if v.co.z < .09 and
            any(body.vertex_groups[g.group].name == 'Foot.'+suffix and g.weight > .99 for g in v.groups)]
        assert soles[suffix], ('Missing boot sole', suffix)
    action(rig, 'Idle')
    for frame in range(1, 62, 3):
        scene.frame_set(frame)
        reset(rig)
        phase = (frame-1)/60*math.tau
        rig.pose.bones['Chest'].rotation_euler.x = .010*math.sin(phase)
        rig.pose.bones['Spine'].rotation_euler.y = .008*math.sin(phase+.4)
        rig.pose.bones['Head'].rotation_euler.y = -.008*math.sin(phase+.4)
        carry_guard(rig, breathing=.002*math.sin(phase))
        key(rig, frame)

    for name,config in GAITS.items():
        last, support = config['last'],config['support']
        action(rig,name)
        previous = {}
        for sample in range(4*(last-1)+1):
            frame = 1+sample*.25
            set_frame(scene,frame)
            reset(rig)
            phase = (frame-1)/(last-1)
            running = name == 'Run'
            targets = {suffix:foot_target(phase+offset,config,soles[suffix],side)
                for suffix,side,offset in [('R',1,0),('L',-1,.5)]}
            if running:
                # One compression/rebound per step, deepest at mid-support.
                hip_height = .858-.006*math.cos(phase*math.tau*2-support*math.tau)
            else:
                # Walking rises over the planted leg instead of crouching on it.
                hip_height = .864+.027*math.sin(phase*math.tau)**2
            sway = (.009 if running else .014)*math.sin(phase*math.tau)
            pelvis = rig.pose.bones['Pelvis']
            pelvis.matrix = Matrix.Translation((sway,0,hip_height-.94)) @ rig.data.bones['Pelvis'].matrix_local
            # Pelvic rotation and the opposite shoulder counter-rotation carry the armour's weight.
            pelvis.rotation_euler.y = (.045 if running else .035)*math.sin(phase*math.tau)
            pelvis.rotation_euler.z = (.022 if running else .018)*math.cos(phase*math.tau)
            rig.pose.bones['Spine'].rotation_euler.x = .065 if running else .025
            rig.pose.bones['Chest'].rotation_euler.y = -.055*math.sin(phase*math.tau)
            rig.pose.bones['Head'].rotation_euler.y = .025*math.sin(phase*math.tau)
            rig.pose.bones['Head'].rotation_euler.x = .035 if running else .015
            bpy.context.view_layer.update()
            for suffix in ('R','L'):
                ankle, rotation = targets[suffix]
                current = solve_limb(rig,'Thigh.'+suffix,'Shin.'+suffix,'Foot.'+suffix,ankle,Vector((0,-1,0)))
                orient(rig,'Foot.'+suffix,current,rotation=rotation)
            carry_guard(rig, phase, running)
            for bone in rig.pose.bones:
                if bone.name in previous:
                    bone.rotation_euler.make_compatible(previous[bone.name])
                previous[bone.name] = bone.rotation_euler.copy()
            key(rig,frame)
    refine_attack_weight_transfer(rig)
    rig.animation_data.action = bpy.data.actions['Idle']
    scene.frame_set(1)


def refine_attack_weight_transfer(rig):
    """Preserve the authored blade arc/impact, replace rigid FK legs with planted IK.

    The pelvis loads the back leg, transfers forward at contact, then settles.
    Sampling the original wrist matrices keeps the .13s impact and trail anchors.
    """
    scene = bpy.context.scene
    clip = bpy.data.actions['Attack']
    rig.animation_data.action = clip
    source = []
    for frame in range(1,14):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        source.append((rig.pose.bones['Hand.R'].matrix.copy(),
                       rig.pose.bones['Chest'].rotation_euler.y))
    ankle_rest = {suffix:rig.data.bones['Foot.'+suffix].head_local.copy() for suffix in ('R','L')}
    # Unity takes the first exported take's first pose as its model defaults.
    # Keep a separate neutral reference so the presented attack can start/end
    # in the same carrying guard as Idle without changing equipment bind poses.
    action(rig,'A_BindPose')
    reset(rig)
    key(rig,1)
    key(rig,2)
    action(rig,'Attack')
    previous = {}
    for frame,(wrist_matrix,chest_turn) in enumerate(source,1):
        scene.frame_set(frame)
        reset(rig)
        if frame in (1,13):
            carry_guard(rig)
            key(rig,frame)
            previous = {bone.name:bone.rotation_euler.copy() for bone in rig.pose.bones}
            continue
        t = (frame-1)/12
        weight = math.sin(math.pi*t)**2
        forward = -.035*math.sin(math.pi*min(1,t/.62)) if t < .62 else -.012*(1-t)/.38
        side_shift = .022*math.sin(math.pi*2*t)*weight
        pelvis = rig.pose.bones['Pelvis']
        pelvis.matrix = Matrix.Translation((side_shift,forward,-.043*weight)) @ rig.data.bones['Pelvis'].matrix_local
        pelvis.rotation_euler.y = chest_turn*.35
        rig.pose.bones['Spine'].rotation_euler.y = chest_turn*.30
        rig.pose.bones['Chest'].rotation_euler = (.035*weight,chest_turn*1.22,0)
        rig.pose.bones['Head'].rotation_euler.y = -chest_turn*.45
        bpy.context.view_layer.update()
        for suffix in ('R','L'):
            ankle = ankle_rest[suffix]
            current = solve_limb(rig,'Thigh.'+suffix,'Shin.'+suffix,'Foot.'+suffix,ankle,Vector((0,-1,0)))
            orient(rig,'Foot.'+suffix,current,rotation=Matrix.Identity(4))
        carry_guard(rig)
        if frame > 8:
            recovery = (frame-8)/5
            recovery = recovery*recovery*(3-2*recovery)
            guard = rig.pose.bones['Hand.R'].matrix
            rotation = wrist_matrix.to_quaternion().slerp(guard.to_quaternion(),recovery)
            position = wrist_matrix.translation.lerp(guard.translation,recovery)
            wrist_matrix = Matrix.Translation(position) @ rotation.to_matrix().to_4x4()
        wrist = wrist_matrix.translation
        current = solve_limb(rig,'UpperArm.R','Forearm.R','Hand.R',wrist,Vector((.40,-.15,-.25)))
        # Keep the blade's exact world orientation, independent from torso counter-rotation.
        hand = rig.pose.bones['Hand.R']
        hand.matrix = wrist_matrix
        hand.location = (0,0,0)
        hand.scale = (1,1,1)
        for bone in rig.pose.bones:
            if bone.name in previous: bone.rotation_euler.make_compatible(previous[bone.name])
            previous[bone.name] = bone.rotation_euler.copy()
        key(rig,frame)


def mirror_anatomy(rig,meshes,markers):
    """Face -Y/up Z means anatomical right is -X.

    D @ deformation @ D reflects complete deformations without negative scales
    or changing the 17 bone names required by the equipment system.
    """
    reflection = Matrix.Diagonal((-1,1,1,1))
    rig.animation_data.action = None
    reset(rig)
    old_rest = {b.name:b.matrix_local.copy() for b in rig.data.bones}
    marker_rest = {m.name:m.matrix_world.copy() for m in markers}
    samples = {}
    scene = bpy.context.scene
    for clip in list(bpy.data.actions):
        rig.animation_data.action = clip
        first,last = map(int,clip.frame_range)
        samples[clip.name] = []
        for sample in range(4*(last-first)+1):
            frame = first+sample*.25
            set_frame(scene,frame)
            bpy.context.view_layer.update()
            samples[clip.name].append((frame,{b.name:b.matrix.copy() for b in rig.pose.bones}))
    rig.animation_data.action = None
    reset(rig)
    rig.data.transform(reflection)
    reset(rig)
    for mesh in meshes:
        mesh.data.transform(reflection)
        for face in mesh.data.polygons:
            face.flip()
        mesh.data.update()
    bpy.context.view_layer.update()
    for marker in markers:
        target = reflection @ marker_rest[marker.name] @ reflection
        marker.matrix_parent_inverse.identity()
        marker.matrix_basis.identity()
        bpy.context.view_layer.update()
        marker.matrix_world = target
        bpy.context.view_layer.update()
        assert (marker.matrix_world.translation-target.translation).length < .0001, (marker.name, 'rest marker offset')
    for name,frames in samples.items():
        action(rig,name)
        previous = {}
        for frame,matrices in frames:
            set_frame(scene,frame)
            reset(rig)
            for bone in rig.pose.bones:
                deformation = matrices[bone.name] @ old_rest[bone.name].inverted()
                bone.matrix = reflection @ deformation @ reflection @ bone.bone.matrix_local
                bone.scale = (1,1,1)
                if bone.name in previous:
                    bone.rotation_euler.make_compatible(previous[bone.name])
                previous[bone.name] = bone.rotation_euler.copy()
                bpy.context.view_layer.update()
            key(rig,frame)
    rig.animation_data.action = bpy.data.actions['Idle']
    scene.frame_set(1)
    bpy.context.view_layer.update()


def validate_rig(rig,body,sword,motion_profile=None):
    scene = bpy.context.scene
    attack_names = ('Attack','Attack2','Attack3','HeavyAttack')
    presented_names = ('Idle','Walk','Run')+attack_names
    assert len(rig.data.bones) == 17
    assert rig.data.bones['Hand.R'].head_local.x < -.45
    assert rig.data.bones['Hand.L'].head_local.x > .45
    assert all(v>0 for v in rig.scale), 'Negative runtime scale'
    assert {'Idle','Walk','Run','Attack'} <= set(bpy.data.actions.keys())
    if motion_profile:
        assert set(motion_profile['clips']) <= set(bpy.data.actions.keys()), 'Motion profile names missing actions'
    multi = sum(sum(g.weight>.001 for g in v.groups)>1 for v in body.data.vertices)
    assert multi>250, ('No flexible joint bands',multi)
    arm_groups = {'UpperArm.'+side for side in ('R','L')} | {'Forearm.'+side for side in ('R','L')} | {'Hand.'+side for side in ('R','L')}
    lower_groups = {'Pelvis','Root'} | {part+'.'+side for part in ('Thigh','Shin','Foot') for side in ('R','L')}
    cross_links = []
    for polygon in body.data.polygons:
        groups = {body.vertex_groups[g.group].name for index in polygon.vertices
                  for g in body.data.vertices[index].groups if g.weight > .001}
        if groups & arm_groups and groups & lower_groups:
            cross_links.append(polygon.index)
    assert not cross_links, ('Arm triangles bound to pelvis/legs',cross_links[:12])
    diagnostics = {'bones':17,'right_hand_rest':list(rig.data.bones['Hand.R'].head_local),
        'left_hand_rest':list(rig.data.bones['Hand.L'].head_local),'joint_blended_vertices':multi,
        'arm_to_lower_body_cross_links':len(cross_links),
        'origin':motion_profile['origin'] if motion_profile else 'Procedural',
        'walk_cycle_m':motion_profile['walk_cycle_m'] if motion_profile else GAITS['Walk']['distance'],
        'run_cycle_m':motion_profile['run_cycle_m'] if motion_profile else GAITS['Run']['distance'],'clips':{}}
    tip,base,grip = [bpy.data.objects[name] for name in ('SwordTip','SwordBase','SwordGrip')]
    glove = [v.co for v in body.data.vertices if v.co.z < 1.03 and
        any(body.vertex_groups[g.group].name == 'Hand.R' and g.weight > .99 for g in v.groups)]
    palm = sum(glove, Vector())/len(glove)
    sole_points = {suffix:[v.co.copy() for v in body.data.vertices if v.co.z < .09 and
        any(body.vertex_groups[g.group].name == 'Foot.'+suffix and g.weight > .99 for g in v.groups)]
        for suffix in ('R','L')}
    rig.animation_data.action = None
    reset(rig)
    tip_index = min(range(len(sword.data.vertices)), key=lambda i:(sword.data.vertices[i].co-tip.matrix_world.translation).length_squared)
    rest_tip_error = (sword.data.vertices[tip_index].co-tip.matrix_world.translation).length
    assert rest_tip_error < .003, ('Mesh/marker rest mismatch',rest_tip_error)
    rig.animation_data.action = bpy.data.actions['Idle']
    set_frame(scene,bpy.data.actions['Idle'].frame_range[0])
    guard = {bone.name:bone.matrix.copy() for bone in rig.pose.bones}
    for clip in bpy.data.actions:
        rig.animation_data.action = clip
        first,last = map(int,clip.frame_range)
        starts = None
        minimum_sole, maximum_support_gap, maximum_plant_slide, maximum_grip_error, maximum_wrist_angle = 99,0,0,0,0
        minimum_ankle_width, maximum_ankle_width, maximum_knee_width = 99,0,0
        previous_support = {}
        maximum_root,maximum_marker_error,min_blade_z,maximum_mesh_tip_error = 0,0,99,0
        maximum_guard_error, maximum_guard_foot_displacement = 0,0
        # Attack guard bridges are checked between the 120 Hz export keys:
        # the old 0.4 s take hid a 3.3 mm sole penetration at sub-key times.
        subdivisions = 16 if motion_profile and clip.name in attack_names else 4
        for sample in range(subdivisions*(last-first)+1):
            frame = first+sample/subdivisions
            set_frame(scene,frame)
            bpy.context.view_layer.update()
            maximum_root = max(maximum_root,rig.pose.bones['Root'].location.length)
            assert maximum_root<.0001, (clip.name,'Root motion')
            if clip.name == 'A_BindPose':
                neutral_error = max(max(abs(b.matrix[r][c]-b.bone.matrix_local[r][c]) for r in range(4) for c in range(4)) for b in rig.pose.bones)
                assert neutral_error < .0001, ('Import/default neutral pose changed',frame,neutral_error)
            assert all(abs(v-1)<.0001 for b in rig.pose.bones for v in b.scale)
            hand = rig.pose.bones['Hand.R']
            hand_deform = hand.matrix @ hand.bone.matrix_local.inverted()
            maximum_grip_error = max(maximum_grip_error,(grip.matrix_world.translation-hand_deform @ palm).length)
            assert maximum_grip_error < .001, (clip.name,'Grip leaves glove',maximum_grip_error)
            if clip.name in presented_names:
                for suffix in ('R','L'):
                    hand, fore = rig.pose.bones['Hand.'+suffix],rig.pose.bones['Forearm.'+suffix]
                    maximum_wrist_angle = max(maximum_wrist_angle,math.degrees((hand.tail-hand.head).angle(fore.tail-fore.head)))
                assert maximum_wrist_angle < 32, (clip.name,'Bent wrist',maximum_wrist_angle)
            if clip.name == 'Idle' or clip.name in attack_names:
                for suffix in ('R','L'):
                    foot = rig.pose.bones['Foot.'+suffix]
                    deform = foot.matrix @ foot.bone.matrix_local.inverted()
                    minimum_sole = min(minimum_sole,min((deform @ point).z for point in sole_points[suffix]))
                assert minimum_sole > -.001, (clip.name,frame,'Boot through floor',minimum_sole)
            if clip.name in attack_names:
                if frame in (first,last):
                    maximum_guard_error = max(maximum_guard_error,
                        max(abs(bone.matrix[r][c]-guard[bone.name][r][c])
                            for bone in rig.pose.bones for r in range(4) for c in range(4)))
                    assert maximum_guard_error < .0001, (clip.name,'Attack endpoints leave Idle guard',maximum_guard_error)
                if motion_profile and motion_profile['clips'][clip.name].get('fixed_attack_feet'):
                    maximum_guard_foot_displacement = max(maximum_guard_foot_displacement,
                        max((rig.pose.bones['Foot.'+suffix].head-guard['Foot.'+suffix].translation).length for suffix in ('R','L')))
                    assert maximum_guard_foot_displacement < .001, (clip.name,'Preserved guard feet moved',maximum_guard_foot_displacement)
            if clip.name in ('Walk','Run'):
                support = GAITS[clip.name]['support']
                cycle = GAITS[clip.name]['distance']
                phase = (frame-first)/(last-first)
                ankle_width = rig.pose.bones['Foot.L'].head.x-rig.pose.bones['Foot.R'].head.x
                knee_width = rig.pose.bones['Shin.L'].head.x-rig.pose.bones['Shin.R'].head.x
                minimum_ankle_width = min(minimum_ankle_width,ankle_width)
                maximum_ankle_width = max(maximum_ankle_width,ankle_width)
                maximum_knee_width = max(maximum_knee_width,knee_width)
                if motion_profile:
                    assert ankle_width > .08, (clip.name,frame,'Feet cross or overlap',ankle_width)
                    assert ankle_width < .38 and knee_width < .38, (clip.name,frame,'Legs spread outside walking tracks',ankle_width,knee_width)
                for suffix,offset in [('R',0),('L',.5)]:
                    foot = rig.pose.bones['Foot.'+suffix]
                    deform = foot.matrix @ foot.bone.matrix_local.inverted()
                    lowest = min((deform @ point).z for point in sole_points[suffix])
                    minimum_sole = min(minimum_sole,lowest)
                    assert lowest > -.001, (clip.name,frame,'Boot through floor',lowest)
                    progress = ((phase+offset)%1)/support
                    if not motion_profile and progress < 1:
                        maximum_support_gap = max(maximum_support_gap,abs(lowest-.002))
                        assert maximum_support_gap < .001, (clip.name,frame,'Floating support sole',lowest)
                    # During flat support the world-space foot must stay fixed as
                    # the capsule advances by the authored distance of the cycle.
                    if not motion_profile and .19 < progress < .71:
                        planted = foot.head.y-cycle*phase
                        if suffix in previous_support:
                            maximum_plant_slide = max(maximum_plant_slide,abs(planted-previous_support[suffix]))
                        previous_support[suffix] = planted
                    else:
                        previous_support.pop(suffix,None)
                assert maximum_plant_slide < .001, (clip.name,'Support foot skating',maximum_plant_slide)
            length = (tip.matrix_world.translation-base.matrix_world.translation).length
            maximum_marker_error = max(maximum_marker_error,abs(length-1.025))
            assert maximum_marker_error<.001, (clip.name,'Blade marker drift',length)
            evaluated = sword.evaluated_get(bpy.context.evaluated_depsgraph_get())
            evaluated_mesh = evaluated.to_mesh()
            mesh_tip = evaluated.matrix_world @ evaluated_mesh.vertices[tip_index].co
            maximum_mesh_tip_error = max(maximum_mesh_tip_error,(mesh_tip-tip.matrix_world.translation).length)
            evaluated.to_mesh_clear()
            assert maximum_mesh_tip_error < .003, (clip.name,'Mesh/marker animation mismatch',maximum_mesh_tip_error)
            min_blade_z = min(min_blade_z,tip.matrix_world.translation.z)
            points = {b.name:b.matrix.copy() for b in rig.pose.bones}
            if starts is None:
                starts = points
            if frame==last and clip.name not in attack_names:
                error = max(max(abs(starts[n][r][c]-points[n][r][c]) for r in range(4) for c in range(4)) for n in starts)
                assert error<.0001, (clip.name,'Loop seam',error)
        if clip.name == 'Attack' and not motion_profile:
            foot_error = 0
            for frame in range(first,last+1):
                scene.frame_set(frame); bpy.context.view_layer.update()
                for suffix in ('R','L'):
                    foot_error = max(foot_error,(rig.pose.bones['Foot.'+suffix].head-rig.data.bones['Foot.'+suffix].head_local).length)
            assert foot_error < .001, ('Attack feet slide',foot_error)
            diagnostics['attack_max_foot_slide_m'] = foot_error
        diagnostics['clips'][clip.name] = {'first_frame':first,'last_frame':last,'duration_s':(last-first)/30,
            'validation_sample_rate_hz':subdivisions*30,
            'max_root_translation_m':maximum_root,'max_blade_length_error_m':maximum_marker_error,'max_mesh_tip_error_m':maximum_mesh_tip_error,'min_blade_tip_height_m':min_blade_z,
            'max_grip_palm_error_m':maximum_grip_error}
        if clip.name in presented_names:
            assert min_blade_z > .02, (clip.name,'Blade through floor',min_blade_z)
            diagnostics['clips'][clip.name]['min_sole_height_m'] = minimum_sole
            diagnostics['clips'][clip.name]['max_wrist_angle_degrees'] = maximum_wrist_angle
        if clip.name in attack_names:
            diagnostics['clips'][clip.name]['max_idle_guard_endpoint_error'] = maximum_guard_error
            if motion_profile:
                presentation = motion_profile['clips'][clip.name]
                assert abs(presentation['duration_s']-(last-first)/30) < 1e-7, (clip.name,'Profile duration differs from action')
                assert abs(presentation['impact_time_s']-.13) < 1e-7, (clip.name,'Authoritative impact moved')
                assert presentation.get('source_contact_timing_error_s',0) < 1e-7, (clip.name,'Source contact retimed away from impact')
            if motion_profile and motion_profile['clips'][clip.name].get('fixed_attack_feet'):
                diagnostics['clips'][clip.name]['max_guard_foot_displacement_m'] = maximum_guard_foot_displacement
            if clip.name != 'Attack' and motion_profile:
                contact = motion_profile['clips'][clip.name]['contact_diagnostics']
                excursion = max((window['central_ankle_planar_excursion_m']
                    for windows in contact['support_world_ankle_motion'].values() for window in windows),default=0)
                assert excursion < .025, (clip.name,'New attack support feet slide',excursion)
                diagnostics['clips'][clip.name]['max_support_ankle_excursion_m'] = excursion
        if clip.name in ('Walk','Run'):
            diagnostics['clips'][clip.name].update(min_sole_height_m=minimum_sole,
                min_ankle_track_width_m=minimum_ankle_width,max_ankle_track_width_m=maximum_ankle_width,
                max_knee_track_width_m=maximum_knee_width)
            if not motion_profile:
                diagnostics['clips'][clip.name].update(max_support_gap_error_m=maximum_support_gap,
                    max_planted_foot_slide_m=maximum_plant_slide)
            else:
                contact = motion_profile['clips'][clip.name]['contact_diagnostics']
                excursion = max((window['central_ankle_planar_excursion_m']
                    for windows in contact['support_world_ankle_motion'].values() for window in windows),default=0)
                assert excursion < .025, (clip.name,'Excessive support ankle excursion',excursion)
                assert contact['maximum_source_target_ankle_error_m'] < .005, (clip.name,'Retargeted foot misses source target',contact)
                diagnostics['clips'][clip.name]['max_support_ankle_excursion_m'] = excursion
                smoothing = motion_profile['clips'][clip.name].get('seam_smoothing',{})
                offset = smoothing.get('maximum_intentional_target_offset_m',0)
                assert offset < .02, (clip.name,'Seam correction changes captured stride excessively',offset)
                diagnostics['clips'][clip.name]['max_seam_ankle_correction_m'] = offset
    rig.animation_data.action = bpy.data.actions['Idle']
    scene.frame_set(1)
    bpy.context.view_layer.update()
    assert base.matrix_world.translation.x<0, 'Sword not in anatomical right hand'
    print('[RIG DIAGNOSTICS]',diagnostics)
    return diagnostics
