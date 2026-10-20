"""Bake verified Mixamo sword/shield motions onto the existing 17-bone rig.

Only actions/pose keys are authored. Skeleton rest matrices, mesh data, weights,
equipment markers and bindings are preserved. Call after mirror_anatomy().
"""
from pathlib import Path
import hashlib
import math
import re

import bpy
from mathutils import Matrix, Quaternion, Vector


FPS = 30
RATE = 120
ATTACK_DURATION = .6
ATTACK_IMPACT_TIME = .13
ATTACK_ENTRY_DURATION = 15/RATE
ATTACK_RECOVERY_DURATION = 24/RATE
ATTACK_WIDE_RECOVERY_DURATION = 34/RATE
ATTACK_PREPARATION_SOURCE_DURATION = .18

CLIPS = {
    'Idle': ('sword and shield idle (4).fbx', None),
    'Walk': ('sword and shield walk.fbx', None),
    'Run': ('sword and shield run.fbx', None),
    'Attack': ('sword and shield slash (3).fbx', ATTACK_DURATION),
}
EXTRA_ATTACKS = {
    'Attack2': {
        'file': 'sword and shield slash.fbx', 'frames': (1, 29),
        'label': 'descending_cut', 'preserve_guard_base': True, 'contact_marker': 'SwordTip',
    },
    'Attack3': {
        'file': 'sword and shield attack (4).fbx', 'frames': (1, 31),
        'label': 'high_cut', 'preserve_guard_base': False, 'contact_marker': 'SwordBase',
    },
    'HeavyAttack': {
        'file': 'sword and shield slash (5).fbx', 'frames': (1, 30),
        'label': 'wide_cut', 'preserve_guard_base': True, 'contact_marker': 'SwordTip',
    },
}
GUARD_BASE_BONES = ('Root', 'Pelvis', 'Thigh.R', 'Thigh.L', 'Shin.R', 'Shin.L', 'Foot.R', 'Foot.L')
UPPER_BODY_BONES = ('Spine', 'Chest', 'Head', 'UpperArm.R', 'Forearm.R', 'Hand.R',
                    'UpperArm.L', 'Forearm.L', 'Hand.L')
MAPPING = {
    'Pelvis': 'Hips', 'Spine': 'Spine', 'Chest': 'Spine2', 'Head': 'Head',
    'UpperArm.R': 'RightArm', 'Forearm.R': 'RightForeArm', 'Hand.R': 'RightHand',
    'UpperArm.L': 'LeftArm', 'Forearm.L': 'LeftForeArm', 'Hand.L': 'LeftHand',
    'Thigh.R': 'RightUpLeg', 'Shin.R': 'RightLeg', 'Foot.R': 'RightFoot',
    'Thigh.L': 'LeftUpLeg', 'Shin.L': 'LeftLeg', 'Foot.L': 'LeftFoot',
}
# Reviewed Slash(3): the fitted blade reaches the front of its swing at about
# source frame 25.9 of frames 1..48, rather than at the earlier wrist crossing.
ATTACK_CONTACT_PROGRESS = .53
UP = Vector((0, 0, 1))
Y_AXIS = Vector((0, 1, 0))


def _canonical(name):
    value = name.rsplit('|', 1)[-1].rsplit(':', 1)[-1]
    return re.sub(r'^mixamorig[0-9_]*', '', value, flags=re.IGNORECASE).lower()


def _frame(value):
    first = math.floor(value)
    bpy.context.scene.frame_set(first, subframe=value-first)
    bpy.context.view_layer.update()


def _reset(rig):
    for bone in rig.pose.bones:
        bone.rotation_mode = 'XYZ'
        bone.matrix_basis.identity()
    bpy.context.view_layer.update()


def _new_action(rig, name):
    previous = bpy.data.actions.get(name)
    rig.animation_data.action = None
    if previous:
        bpy.data.actions.remove(previous)
    result = bpy.data.actions.new(name)
    result.use_fake_user = True
    rig.animation_data.action = result
    return result


def _key(rig, frame, previous):
    for bone in rig.pose.bones:
        if bone.name in previous:
            bone.rotation_euler.make_compatible(previous[bone.name])
        previous[bone.name] = bone.rotation_euler.copy()
        bone.keyframe_insert(data_path='rotation_euler', frame=frame, group=bone.name)
        bone.keyframe_insert(data_path='location', frame=frame, group=bone.name)


def _set_rotation(rig, name, rotation):
    bone = rig.pose.bones[name]
    head = bone.head.copy()
    bone.matrix = Matrix.Translation(head) @ rotation.to_matrix().to_4x4()
    bone.location = (0, 0, 0)
    bone.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def _solve_leg(rig, suffix, target, knee_direction, rotations):
    requested = target.copy()
    upper, lower, foot = ['Thigh.'+suffix, 'Shin.'+suffix, 'Foot.'+suffix]
    origin = rig.pose.bones[upper].head.copy()
    first, second = rig.data.bones[upper].length, rig.data.bones[lower].length
    delta = target-origin
    distance = delta.length
    maximum = first+second-.0002
    if distance >= maximum:
        # Source and target hip widths differ. A small residual reach correction
        # remains on the same ray, never flips a knee or changes bone lengths.
        assert distance-maximum < .045, ('Mixamo leg unreachable', suffix, distance, maximum)
        target = origin+delta.normalized()*maximum
        delta, distance = target-origin, maximum
    assert distance > abs(first-second)+.0002, ('Mixamo folded leg', suffix, distance)
    axis = delta.normalized()
    projection = (first*first-second*second+distance*distance)/(2*distance)
    altitude = math.sqrt(max(0, first*first-projection*projection))
    bend = knee_direction-axis*knee_direction.dot(axis)
    assert bend.length > 1e-6, ('Missing source knee plane', suffix)
    knee = origin+axis*projection+bend.normalized()*altitude
    for name, start, end in ((upper, origin, knee), (lower, knee, target)):
        rotation = rotations[name]
        swing = (rotation @ Y_AXIS).rotation_difference((end-start).normalized())
        _set_rotation(rig, name, swing @ rotation)
    _set_rotation(rig, foot, rotations[foot])
    return (rig.pose.bones[foot].head-requested).length


def _apply_pose(rig, pose):
    for name, (location, rotation) in pose.items():
        bone = rig.pose.bones[name]
        bone.location = location
        bone.rotation_euler = rotation.to_euler('XYZ')
        bone.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def _average_rotation(rotations, weights, reference):
    """Average nearby rotations in the reference's tangent space, avoiding flips."""
    logarithm = Vector()
    for rotation, weight in zip(rotations, weights):
        delta = reference.inverted() @ rotation
        if delta.w < 0:
            delta.negate()
        angle = delta.angle
        if angle > 1e-7:
            logarithm += delta.axis*(angle*weight)
    if logarithm.length < 1e-8:
        return reference.copy()
    return reference @ Quaternion(logarithm.normalized(), logarithm.length)


def _smooth_gait_seam(rig, poses, duration, soles, targets):
    """Spread a noisy mocap wrap over 50 ms on either side, preserving contacts.

    Cubic endpoint tangents alone concentrate the velocity change in one 120 Hz
    interval. A short circular filter gives that change a finite time window.
    Only this window changes; the captured motion in the middle remains intact.
    """
    count = len(poses)-1
    step, sigma, window, plateau = duration/count, .015, .05, .025
    radius = math.ceil(3*sigma/step)
    offsets = list(range(-radius, radius+1))
    weights = [math.exp(-.5*(offset*step/sigma)**2) for offset in offsets]
    total = sum(weights)
    weights = [weight/total for weight in weights]
    original = [{name: (location.copy(), rotation.copy()) for name, (location, rotation) in pose.items()}
                for pose in poses]
    feet = {suffix: [] for suffix in ('R', 'L')}
    for pose in original[:-1]:
        _apply_pose(rig, pose)
        for suffix in feet:
            upper, lower, foot = [rig.pose.bones[name+'.'+suffix] for name in ('Thigh', 'Shin', 'Foot')]
            axis = (foot.head-upper.head).normalized()
            bend = lower.head-upper.head
            bend -= axis*bend.dot(axis)
            deformation = foot.matrix.to_3x3() @ foot.bone.matrix_local.to_3x3().inverted()
            clearance = foot.head.z+min((deformation @ point).z for point in soles[suffix])
            feet[suffix].append((foot.head.copy(), bend, clearance))
    maximum_change, maximum_target_offset = 0, 0
    changed_samples = 0
    for index in range(count):
        distance = min(index, count-index)*step
        if distance >= window:
            continue
        blend = 1 if distance <= plateau else 1-_smoother((distance-plateau)/(window-plateau))
        neighbours = [(index+offset) % count for offset in offsets]
        for name, (location, rotation) in original[index].items():
            average = sum((original[other][name][0]*weight for other, weight in zip(neighbours, weights)), Vector())
            average_rotation = _average_rotation([original[other][name][1] for other in neighbours], weights, rotation)
            poses[index][name] = (location.lerp(average, blend), rotation.slerp(average_rotation, blend))
        _apply_pose(rig, poses[index])
        for suffix in feet:
            original_position, original_bend, original_clearance = feet[suffix][index]
            average_position = sum((feet[suffix][other][0]*weight for other, weight in zip(neighbours, weights)), Vector())
            average_bend = sum((feet[suffix][other][1]*weight for other, weight in zip(neighbours, weights)), Vector())
            average_clearance = sum(feet[suffix][other][2]*weight for other, weight in zip(neighbours, weights))
            target = original_position.lerp(average_position, blend)
            clearance = original_clearance*(1-blend)+average_clearance*blend
            foot = rig.pose.bones['Foot.'+suffix]
            deformation = foot.matrix.to_3x3() @ foot.bone.matrix_local.to_3x3().inverted()
            target.z = clearance-min((deformation @ point).z for point in soles[suffix])
            maximum_target_offset = max(maximum_target_offset, (target-targets[suffix][index]).length)
            targets[suffix][index] = target.copy()
            rotations = {name+'.'+suffix: rig.pose.bones[name+'.'+suffix].matrix.to_quaternion()
                         for name in ('Thigh', 'Shin', 'Foot')}
            _solve_leg(rig, suffix, target, original_bend.lerp(average_bend, blend), rotations)
            maximum_change = max(maximum_change, (rig.pose.bones['Foot.'+suffix].head-original_position).length)
        poses[index] = {bone.name: (bone.location.copy(), bone.rotation_euler.to_quaternion()) for bone in rig.pose.bones}
        changed_samples += 1
    poses[-1] = {name: (location.copy(), rotation.copy()) for name, (location, rotation) in poses[0].items()}
    for suffix in targets:
        targets[suffix][-1] = targets[suffix][0].copy()
    return {'method': 'localized_periodic_quaternion_filter_with_contact_preserving_leg_ik',
            'half_window_s': window, 'gaussian_sigma_s': sigma, 'changed_samples': changed_samples,
            'maximum_ankle_change_m': maximum_change,
            'maximum_intentional_target_offset_m': maximum_target_offset}


def _stable_source_poles(samples, destination_rest, align, source_leg_length):
    """A nearly straight source leg has an unreliable positional knee plane.

    The target's different proportions may require much more knee flexion. Do
    not amplify a few millimetres of source knee noise into a lateral kick:
    recover its plane from the captured thigh orientation as the source bend
    tends to zero. Normal flexed poses retain the measured source knee plane.
    """
    threshold = source_leg_length*.06
    forward = Vector((0, -1, 0))
    minimum_bend = 99
    for sample in samples:
        poles = {}
        for suffix, side in (('R', 'Right'), ('L', 'Left')):
            points = sample['points']
            source_axis = (points[side+'Foot']-points[side+'UpLeg']).normalized()
            bend = points[side+'Leg']-points[side+'UpLeg']
            bend -= source_axis*bend.dot(source_axis)
            minimum_bend = min(minimum_bend, bend.length)
            axis = align @ source_axis
            deformation = sample['rotations']['Thigh.'+suffix] @ destination_rest['Thigh.'+suffix].to_quaternion().inverted()
            reference = deformation @ forward
            reference -= axis*reference.dot(axis)
            assert reference.length > 1e-5, ('Invalid captured thigh plane', suffix)
            reference.normalize()
            confidence = _smoother(bend.length/threshold)
            measured = (align @ bend).normalized() if bend.length > 1e-7 else reference
            pole = reference*(1-confidence)+measured*confidence
            poles[suffix] = pole.normalized() if pole.length > 1e-5 else reference
        sample['knee_poles'] = poles
    return {'method': 'captured_thigh_plane_fallback_for_nearly_straight_source_leg',
            'source_bend_blend_threshold_m': threshold, 'minimum_source_knee_offset_m': minimum_bend}


def _final_contacts(rig, soles, duration, distance, targets, native_targets):
    """Measure final keyed actions, including guard blends and loop closure."""
    count = round(duration*RATE)
    samples = {suffix: [] for suffix in ('R', 'L')}
    maximum_error, maximum_native_difference, minimum_sole = 0, 0, 99
    for index in range(count+1):
        _frame(1+duration*FPS*index/count)
        for suffix in ('R', 'L'):
            foot = rig.pose.bones['Foot.'+suffix]
            deformation = foot.matrix.to_3x3() @ foot.bone.matrix_local.to_3x3().inverted()
            points = [foot.head+deformation @ point for point in soles[suffix]]
            lowest = min(point.z for point in points)
            minimum_sole = min(minimum_sole, lowest)
            maximum_error = max(maximum_error, (foot.head-targets[suffix][index]).length)
            maximum_native_difference = max(maximum_native_difference, (foot.head-native_targets[suffix][index]).length)
            ankle_world = foot.head.copy()
            ankle_world.y -= distance*index/count
            samples[suffix].append((lowest, ankle_world))
    contacts, reports = {}, {}
    for suffix, values in samples.items():
        active, windows, slides = None, [], []
        for index, (height, _) in enumerate(values):
            supported = height <= .008
            if supported and active is None:
                active = index
            if active is not None and (not supported or index == count):
                end = index if supported else index-1
                if end-active >= 2:
                    windows.append([active/count*duration, end/count*duration])
                    central_first = active+round((end-active)*.15)
                    central_last = end-round((end-active)*.15)
                    positions = [value[1] for value in values[central_first:central_last+1]]
                    origin = positions[0]
                    planar = [Vector((point.x-origin.x, point.y-origin.y, 0)) for point in positions]
                    slides.append({'start_s': windows[-1][0], 'end_s': windows[-1][1],
                                   'central_ankle_planar_excursion_m': max(point.length for point in planar),
                                   'central_ankle_planar_rms_m': math.sqrt(sum(point.length_squared for point in planar)/len(planar))})
                active = None
        contacts[suffix] = windows
        reports[suffix] = slides
    return contacts, {'measurement': 'final_keyed_action_120Hz_actual_boot_sole',
                      'support_threshold_m': .008, 'minimum_sole_height_m': minimum_sole,
                      'target_reference': 'retargeted_contact_targets_after_declared_seam_filters_and_guard_bridges',
                      'maximum_target_ankle_ik_error_m': maximum_error,
                      'maximum_source_target_ankle_error_m': maximum_error,
                      'maximum_native_source_ankle_difference_m': maximum_native_difference,
                      'support_world_ankle_motion': reports,
                      'slide_note': 'Ankle excursion includes heel/toe rocking; use central support and visual review.'}


def _source(path):
    existing_objects, existing_actions = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True, automatic_bone_orientation=False)
    objects = set(bpy.data.objects)-existing_objects
    actions = set(bpy.data.actions)-existing_actions
    candidates = [obj for obj in objects if obj.type == 'ARMATURE']
    assert len(candidates) == 1, ('Expected one Mixamo rig', str(path), len(candidates))
    rig = candidates[0]
    names = {}
    for expected in set(MAPPING.values()) | {'RightToeBase', 'LeftToeBase'}:
        matches = [bone.name for bone in rig.data.bones if _canonical(bone.name) == expected.lower()]
        assert len(matches) == 1, ('Missing/ambiguous Mixamo bone', str(path), expected, matches)
        names[expected] = matches[0]
    assert rig.animation_data and rig.animation_data.action, ('Missing Mixamo action', str(path))
    action = rig.animation_data.action
    fps = bpy.context.scene.render.fps/bpy.context.scene.render.fps_base
    first, last = map(float, action.frame_range)
    rest = {name: rig.matrix_world @ rig.data.bones[actual].matrix_local for name, actual in names.items()}
    return rig, action, names, rest, first, last, fps, objects, actions


def _smoother(value):
    value = min(1, max(0, value))
    return value**3*(10-15*value+6*value*value)


def _time_attack(progress, contact, source_duration):
    """Smooth monotonic Hermite warp with the unchanged authoritative impact."""
    # Only the final 180 ms of captured preparation precede the hit. Replaying
    # the entire 0.5-0.8 s wind-up in 130 ms creates an implausible extra swing.
    start = max(0, contact-ATTACK_PREPARATION_SOURCE_DURATION/source_duration)
    contact = (contact-start)/(1-start)
    pivot = ATTACK_IMPACT_TIME/ATTACK_DURATION
    slopes = (contact/pivot, (1-contact)/(1-pivot))
    tangent = 2*slopes[0]*slopes[1]/(slopes[0]+slopes[1])
    if progress <= pivot:
        x, y0, y1, span, m0, m1 = progress/pivot, 0, contact, pivot, slopes[0], tangent
    else:
        x, y0, y1, span, m0, m1 = (progress-pivot)/(1-pivot), contact, 1, 1-pivot, tangent, slopes[1]
    warped = (2*x**3-3*x*x+1)*y0+(x**3-2*x*x+x)*span*m0+(-2*x**3+3*x*x)*y1+(x**3-x*x)*span*m1
    return start+(1-start)*warped


def _rotation_log(reference, rotation, near=None):
    delta = reference.inverted() @ rotation
    if delta.w < 0:
        delta.negate()
    logarithm = delta.axis*delta.angle if delta.angle > 1e-7 else Vector()
    if near is not None and logarithm.length > 1e-7:
        axis = logarithm.normalized()
        logarithm = min((logarithm+axis*math.tau*turn for turn in (-1, 0, 1)),
                        key=lambda value: (value-near).length_squared)
    return logarithm


def _rotation_exp(reference, logarithm):
    return (reference @ Quaternion(logarithm.normalized(), logarithm.length)
            if logarithm.length > 1e-8 else reference.copy())


def _hermite(start, end, start_velocity, end_velocity, progress, duration):
    x = progress
    return ((2*x**3-3*x*x+1)*start+(x**3-2*x*x+x)*duration*start_velocity
            +(-2*x**3+3*x*x)*end+(x**3-x*x)*duration*end_velocity)


def _attack_guard_transitions(rig, poses, idle_guard, duration, soles, recovery_duration=ATTACK_RECOVERY_DURATION):
    """Fixed-reference quaternion Hermite bridges, with captured C1 tangents.

    Blending a moving captured rotation to guard can flip its shortest arc as
    it crosses 180 degrees. Each bridge instead has one fixed pair of endpoint
    rotations and matches the captured velocity at its join. The active swing
    and its authoritative contact are retained between the two bridges.
    """
    count = len(poses)-1
    step = duration/count
    entry = round(ATTACK_ENTRY_DURATION/step)
    recovery = round((duration-recovery_duration)/step)
    original = [{name: (location.copy(), rotation.copy()) for name, (location, rotation) in pose.items()}
                for pose in poses]
    feet = {suffix: [] for suffix in ('R', 'L')}
    world_rotations = []
    for pose in original:
        _apply_pose(rig, pose)
        world_rotations.append({bone.name: bone.matrix.to_quaternion() for bone in rig.pose.bones})
        for suffix in feet:
            thigh, shin, foot = [rig.pose.bones[name+'.'+suffix] for name in ('Thigh', 'Shin', 'Foot')]
            axis = (foot.head-thigh.head).normalized()
            bend = shin.head-thigh.head
            bend -= axis*bend.dot(axis)
            deformation = foot.matrix.to_3x3() @ foot.bone.matrix_local.to_3x3().inverted()
            clearance = foot.head.z+min((deformation @ point).z for point in soles[suffix])
            feet[suffix].append((foot.head.copy(), bend, clearance))
    _apply_pose(rig, idle_guard)
    final_targets = {suffix: [value[0].copy() for value in values] for suffix, values in feet.items()}
    guard_world_rotations = {bone.name: bone.matrix.to_quaternion() for bone in rig.pose.bones}
    guard_feet = {}
    for suffix in feet:
        thigh, shin, foot = [rig.pose.bones[name+'.'+suffix] for name in ('Thigh', 'Shin', 'Foot')]
        axis = (foot.head-thigh.head).normalized()
        bend = shin.head-thigh.head
        bend -= axis*bend.dot(axis)
        deformation = foot.matrix.to_3x3() @ foot.bone.matrix_local.to_3x3().inverted()
        guard_feet[suffix] = (foot.head.copy(), bend,
                             foot.head.z+min((deformation @ point).z for point in soles[suffix]))
    for first, last, entering in ((0, entry, True), (recovery, count, False)):
        anchor = last if entering else first
        span = (last-first)*step
        for index in range(first, last+1):
            progress = (index-first)/(last-first)
            for name, (guard_location, guard_rotation) in idle_guard.items():
                location, rotation = original[anchor][name]
                location_velocity = (original[anchor+1][name][0]-original[anchor-1][name][0])/(2*step)
                hand_local = name.startswith('Hand.')
                captured_rotation = rotation if hand_local else world_rotations[anchor][name]
                guard_world_rotation = guard_rotation if hand_local else guard_world_rotations[name]
                reference = guard_world_rotation if entering else captured_rotation
                end_rotation = captured_rotation if entering else guard_world_rotation
                end_log = _rotation_log(reference, end_rotation)
                after = original[anchor+1][name][1] if hand_local else world_rotations[anchor+1][name]
                before = original[anchor-1][name][1] if hand_local else world_rotations[anchor-1][name]
                angular_velocity = (_rotation_log(reference, after, end_log if entering else Vector())
                                    -_rotation_log(reference, before, end_log if entering else Vector()))/(2*step)
                location_result = _hermite(guard_location if entering else location,
                                           location if entering else guard_location,
                                           Vector() if entering else location_velocity,
                                           location_velocity if entering else Vector(), progress, span)
                rotation_result = _rotation_exp(reference, _hermite(Vector(), end_log,
                                           Vector() if entering else angular_velocity,
                                           angular_velocity if entering else Vector(), progress, span))
                poses[index][name] = (location_result, rotation)
                # Interpolate in the armature's world orientation, so a torso
                # turn cannot add another shortest-arc turn to the hand return.
                bone = rig.pose.bones[name]
                bone.location = location_result
                bpy.context.view_layer.update()
                if hand_local:
                    bone.rotation_euler = rotation_result.to_euler('XYZ')
                else:
                    bone.matrix = Matrix.Translation(bone.head) @ rotation_result.to_matrix().to_4x4()
                bone.scale = (1, 1, 1)
                bpy.context.view_layer.update()
            # Interpolating local hip/knee angles alone drives boots through the
            # floor. Preserve the corresponding world ankle bridge and sole
            # clearance with the same fixed skeleton and ordinary two-bone IK.
            for suffix, values in feet.items():
                guard_point, guard_bend, guard_clearance = guard_feet[suffix]
                point, bend, clearance = values[anchor]
                velocity = (values[anchor+1][0]-values[anchor-1][0])/(2*step)
                target = _hermite(guard_point if entering else point, point if entering else guard_point,
                                  Vector() if entering else velocity,
                                  velocity if entering else Vector(), progress, span)
                blend = _smoother(progress if entering else 1-progress)
                desired_clearance = guard_clearance*(1-blend)+clearance*blend
                if (Vector((point.x, point.y, 0))-Vector((guard_point.x, guard_point.y, 0))).length > .025:
                    # A bridge between two different planted positions needs
                    # a small real step, rather than a boot sliding on the floor.
                    desired_clearance = max(desired_clearance, guard_clearance+.03*math.sin(math.pi*progress)**2)
                foot = rig.pose.bones['Foot.'+suffix]
                deformation = foot.matrix.to_3x3() @ foot.bone.matrix_local.to_3x3().inverted()
                target.z = desired_clearance-min((deformation @ vertex).z for vertex in soles[suffix])
                final_targets[suffix][index] = target.copy()
                rotations = {name+'.'+suffix: rig.pose.bones[name+'.'+suffix].matrix.to_quaternion()
                             for name in ('Thigh', 'Shin', 'Foot')}
                _solve_leg(rig, suffix, target, guard_bend.lerp(bend, blend), rotations)
            poses[index] = {bone.name: (bone.location.copy(), bone.rotation_euler.to_quaternion())
                            for bone in rig.pose.bones}
    poses[0] = {name: (location.copy(), rotation.copy()) for name, (location, rotation) in idle_guard.items()}
    poses[-1] = {name: (location.copy(), rotation.copy()) for name, (location, rotation) in idle_guard.items()}
    return final_targets


def _attack_presentation(first, last, source_fps, contact, recovery):
    source_duration = (last-first)/source_fps
    core_start = _time_attack(ATTACK_ENTRY_DURATION/ATTACK_DURATION, contact, source_duration)
    core_end = _time_attack((ATTACK_DURATION-recovery)/ATTACK_DURATION, contact, source_duration)
    return {
        'source_contact_frame': first+(last-first)*contact,
        'source_contact_timing_error_s': abs(_time_attack(ATTACK_IMPACT_TIME/ATTACK_DURATION,
                                                        contact, source_duration)-contact)*source_duration,
        'captured_active_window_s': [ATTACK_ENTRY_DURATION, ATTACK_DURATION-recovery],
        'captured_active_source_frame_range': [first+(last-first)*core_start, first+(last-first)*core_end],
        'preparation_method': 'C1_guard_to_captured_preimpact_pose_and_velocity',
        'recovery_method': 'C1_fixed_captured_pose_and_velocity_to_guard',
        'transition_orientation_space': 'world_orientation_except_anatomical_local_wrists',
        'transition_foot_adaptation': 'world_ankle_hermite_bridge_with_sole_clearance_and_up_to_30mm_step_when_planted_positions_differ',
    }


def _snapshot(rig, body):
    return {
        'rest': {bone.name: bone.matrix_local.copy() for bone in rig.data.bones},
        'parents': {bone.name: bone.parent.name if bone.parent else None for bone in rig.data.bones},
        'vertices': [vertex.co.copy() for vertex in body.data.vertices],
        'markers': {name: (bpy.data.objects[name].matrix_parent_inverse.copy(),
                           bpy.data.objects[name].matrix_basis.copy())
                    for name in ('SwordBase', 'SwordTip', 'SwordGrip')},
    }


def _assert_preserved(rig, body, snapshot):
    assert len(rig.data.bones) == len(snapshot['rest']) == 17
    for bone in rig.data.bones:
        assert bone.name in snapshot['rest']
        assert (bone.parent.name if bone.parent else None) == snapshot['parents'][bone.name]
        assert max(abs(bone.matrix_local[r][c]-snapshot['rest'][bone.name][r][c])
                   for r in range(4) for c in range(4)) < 1e-7, ('Rest changed', bone.name)
    assert len(body.data.vertices) == len(snapshot['vertices'])
    assert max((vertex.co-point).length for vertex, point in zip(body.data.vertices, snapshot['vertices'])) < 1e-7
    for name, (parent_inverse, basis) in snapshot['markers'].items():
        obj = bpy.data.objects[name]
        assert max(abs(obj.matrix_parent_inverse[r][c]-parent_inverse[r][c]) for r in range(4) for c in range(4)) < 1e-7
        assert max(abs(obj.matrix_basis[r][c]-basis[r][c]) for r in range(4) for c in range(4)) < 1e-7


def _retarget_extra_attacks(rig, source_dir, destination_rest, soles, idle_guard):
    """Add real library variants without touching the established four actions.

    The two cuts whose source lunges cannot fit a fixed capsule use an ordinary
    upper-body attack layer over the existing guard base. The high cut retains
    its captured lower body because its measured contacts already fit in place.
    """
    result = {}
    for clip_name, specification in EXTRA_ATTACKS.items():
        path = source_dir/specification['file']
        assert path.is_file(), ('Missing selected attack source', str(path))
        source, source_action, names, source_rest, original_first, original_last, fps, objects, source_actions = _source(path)
        first, last = specification['frames']
        assert original_first <= first < last <= original_last, ('Attack crop outside source', clip_name)
        source_length = sum((source_rest[a].translation-source_rest[b].translation).length
                            for a, b in (('RightUpLeg', 'RightLeg'), ('RightLeg', 'RightFoot')))
        scale = (rig.data.bones['Thigh.R'].length+rig.data.bones['Shin.R'].length)/source_length
        source_right = (source_rest['RightUpLeg'].translation-source_rest['LeftUpLeg'].translation).normalized()
        target_right = (destination_rest['Thigh.R'].translation-destination_rest['Thigh.L'].translation).normalized()
        align = source_right.rotation_difference(target_right)
        references = {}
        for target, original in MAPPING.items():
            reference = destination_rest[target].to_quaternion()
            if target.startswith(('UpperArm.', 'Forearm.', 'Hand.')):
                direction = align @ (source_rest[original].to_quaternion() @ Y_AXIS)
                reference = (reference @ Y_AXIS).rotation_difference(direction) @ reference
            references[target] = reference
        _frame(first)
        start = (source.matrix_world @ source.pose.bones[names['Hips']].matrix).translation.copy()
        _frame(last)
        travel = (source.matrix_world @ source.pose.bones[names['Hips']].matrix).translation-start
        travel.z = 0
        source_count = math.ceil((last-first)/fps*RATE)
        captured, markers, captured_targets = [], [], {'R': [], 'L': []}
        for index in range(source_count+1):
            progress = index/source_count
            _frame(first+(last-first)*progress)
            world = source.matrix_world
            points = {name: world @ source.pose.bones[actual].matrix.translation for name, actual in names.items()}
            rotations = {target: (align @ (world @ source.pose.bones[names[original]].matrix).to_quaternion()
                         @ source_rest[original].to_quaternion().inverted() @ align.inverted() @ references[target])
                         for target, original in MAPPING.items()}
            if specification['preserve_guard_base']:
                _apply_pose(rig, idle_guard)
            else:
                _reset(rig)
                displacement = points['Hips']-source_rest['Hips'].translation
                displacement.x -= start.x
                displacement.y -= start.y
                displacement -= travel*progress
                pelvis = rig.pose.bones['Pelvis']
                pelvis.matrix = Matrix.Translation(destination_rest['Pelvis'].translation+align @ displacement*scale) @ rotations['Pelvis'].to_matrix().to_4x4()
                pelvis.scale = (1, 1, 1)
                bpy.context.view_layer.update()
            for target in UPPER_BODY_BONES:
                _set_rotation(rig, target, rotations[target])
            for suffix in ('R', 'L'):
                hand, forearm = rig.pose.bones['Hand.'+suffix], rig.pose.bones['Forearm.'+suffix]
                hand_axis, forearm_axis = (hand.tail-hand.head).normalized(), (forearm.tail-forearm.head).normalized()
                angle = hand_axis.angle(forearm_axis)
                if angle > math.radians(30):
                    desired = forearm_axis.slerp(hand_axis, math.radians(30)/angle)
                    _set_rotation(rig, hand.name, hand_axis.rotation_difference(desired) @ hand.matrix.to_quaternion())
            if not specification['preserve_guard_base']:
                for suffix, side in (('R', 'Right'), ('L', 'Left')):
                    foot_deformation = rotations['Foot.'+suffix] @ destination_rest['Foot.'+suffix].to_quaternion().inverted()
                    minimum = min((foot_deformation @ point).z for point in soles[suffix])
                    clearance = max(0, min(points[side+'Foot'].z-source_rest[side+'Foot'].translation.z,
                                          points[side+'ToeBase'].z-source_rest[side+'ToeBase'].translation.z))
                    ankle = align @ (points[side+'Foot']-Vector((start.x, start.y, 0))-travel*progress)*scale
                    ankle.z = -minimum+.002+clearance*scale
                    source_axis = (points[side+'Foot']-points[side+'UpLeg']).normalized()
                    bend = points[side+'Leg']-points[side+'UpLeg']
                    bend -= source_axis*bend.dot(source_axis)
                    _solve_leg(rig, suffix, ankle, align @ bend, rotations)
            captured.append({bone.name: (bone.location.copy(), bone.rotation_euler.to_quaternion()) for bone in rig.pose.bones})
            for suffix in captured_targets:
                captured_targets[suffix].append(rig.pose.bones['Foot.'+suffix].head.copy())
            markers.append(bpy.data.objects[specification['contact_marker']].matrix_world.translation.copy())
        active = [index for index in range(source_count+1) if .15 <= index/source_count <= .85]
        contact_index = min(active, key=lambda index: markers[index].y)
        contact = contact_index/source_count
        count, duration = round(ATTACK_DURATION*RATE), ATTACK_DURATION
        baked, targets = [], {'R': [], 'L': []}
        for index in range(count+1):
            progress = index/count
            source_progress = _time_attack(progress, contact, (last-first)/fps)
            position = source_progress*source_count
            lower = min(source_count-1, math.floor(position))
            fraction = position-lower
            pose = {name: (location.lerp(captured[lower+1][name][0], fraction),
                           rotation.slerp(captured[lower+1][name][1], fraction))
                    for name, (location, rotation) in captured[lower].items()}
            baked.append(pose)
            for suffix in targets:
                targets[suffix].append(captured_targets[suffix][lower].lerp(captured_targets[suffix][lower+1], fraction))
        recovery_duration = ATTACK_WIDE_RECOVERY_DURATION if clip_name in ('Attack2', 'HeavyAttack') else ATTACK_RECOVERY_DURATION
        native_targets = {suffix: [point.copy() for point in points] for suffix, points in targets.items()}
        targets = _attack_guard_transitions(rig, baked, idle_guard, duration, soles, recovery_duration)
        _new_action(rig, clip_name)
        previous = {}
        for index, pose in enumerate(baked):
            frame = 1+duration*FPS*index/count
            _frame(frame)
            _apply_pose(rig, pose)
            for name, (_, rotation) in pose.items():
                if name in previous:
                    rig.pose.bones[name].rotation_euler = rotation.to_euler('XYZ', previous[name])
            _key(rig, frame, previous)
        contacts, contact_diagnostics = _final_contacts(rig, soles, duration, 0, targets, native_targets)
        if specification['preserve_guard_base']:
            contact_diagnostics['maximum_native_source_ankle_difference_m'] = None
            contact_diagnostics['native_source_leg_fidelity'] = 'not_applicable_preserved_idle_guard_lower_body'
        result[clip_name] = {
            'source_file': path.name, 'source_sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
            'source_action': source_action.name, 'source_fps': fps,
            'source_original_frame_range': [original_first, original_last], 'source_frame_range': [first, last],
            'source_duration_s': (last-first)/fps, 'duration_s': duration, 'first_frame': 1, 'last_frame': 1+duration*FPS,
            'loop': False, 'retarget_leg_scale': scale, 'source_forward_travel_m': -travel.y,
            'attack_style': specification['label'], 'impact_time_s': ATTACK_IMPACT_TIME,
            'guard_entry_duration_s': ATTACK_ENTRY_DURATION, 'guard_recovery_duration_s': recovery_duration,
            'guard_transition_method': 'fixed_world_quaternion_hermite_C1_with_sole_preserving_ik',
            'preparation_source_duration_s': ATTACK_PREPARATION_SOURCE_DURATION,
            'presentation_preparation_start_frame': first+(last-first)*max(0, contact-ATTACK_PREPARATION_SOURCE_DURATION/((last-first)/fps)),
            'source_contact_progress': contact, 'source_contact_frame': first+(last-first)*contact,
            'source_contact_marker': specification['contact_marker'],
            'source_contact_selection': 'maximum fitted marker forward reach in active cropped source interval',
            'animation_layer': 'captured_upper_body_on_idle_guard_base' if specification['preserve_guard_base'] else 'captured_full_body',
            'preserved_idle_bones': list(GUARD_BASE_BONES) if specification['preserve_guard_base'] else [],
            'captured_bones': list(UPPER_BODY_BONES) if specification['preserve_guard_base'] else list(MAPPING),
            'fixed_attack_feet': specification['preserve_guard_base'],
            'contact_intervals_s': contacts, 'contact_method': 'final_keyed_actual_boot_clearance',
            'contact_diagnostics': contact_diagnostics,
            'body_spin_360': False, 'gameplay_coverage_degrees': 360 if clip_name == 'HeavyAttack' else 130,
        }
        result[clip_name].update(_attack_presentation(first, last, fps, contact, recovery_duration))
        for obj in objects:
            bpy.data.objects.remove(obj, do_unlink=True)
        for action in source_actions:
            bpy.data.actions.remove(action)
        bpy.context.scene.render.fps = FPS
        bpy.context.scene.render.fps_base = 1
        print('[MIXAMO ATTACK BAKED]', clip_name, path.name, (first, last), 'contact frame', result[clip_name]['source_contact_frame'])
    return result


def retarget_mixamo(rig, body, source_dir):
    source_dir = Path(source_dir).resolve()
    assert rig.data.bones['Hand.R'].head_local.x < 0, 'Call Mixamo retarget after mirror_anatomy'
    assert bpy.data.actions.get('A_BindPose'), 'Missing preserved neutral import take'
    snapshot = _snapshot(rig, body)
    destination_rest = snapshot['rest']
    soles = {}
    for suffix in ('R', 'L'):
        ankle = rig.data.bones['Foot.'+suffix].head_local
        soles[suffix] = [vertex.co-ankle for vertex in body.data.vertices if vertex.co.z < .09 and
                         any(body.vertex_groups[group.group].name == 'Foot.'+suffix and group.weight > .99
                             for group in vertex.groups)]
        assert soles[suffix], ('Missing destination sole', suffix)
    metadata = {'origin': 'Mixamo', 'source_pack': 'SwordAndShieldPack', 'bake_fps': FPS,
                'sample_rate_hz': RATE, 'root_motion': False, 'clips': {}}
    idle_guard = None
    for clip_name, (filename, forced_duration) in CLIPS.items():
        path = source_dir/filename
        assert path.is_file(), ('Missing downloaded Mixamo file', str(path))
        source, source_action, names, source_rest, first, last, source_fps, objects, source_actions = _source(path)
        duration = forced_duration or (last-first)/source_fps
        leg_source = sum((source_rest[a].translation-source_rest[b].translation).length
                         for a, b in (('RightUpLeg', 'RightLeg'), ('RightLeg', 'RightFoot')))
        leg_target = rig.data.bones['Thigh.R'].length+rig.data.bones['Shin.R'].length
        scale = leg_target/leg_source
        # Both imported FBX and the corrected HeroRig face -Y/up Z. A rigid base
        # change remains explicit so source object rotations/scales are honored.
        source_right = (source_rest['RightUpLeg'].translation-source_rest['LeftUpLeg'].translation).normalized()
        target_right = (destination_rest['Thigh.R'].translation-destination_rest['Thigh.L'].translation).normalized()
        align = source_right.rotation_difference(target_right)
        assert abs((align @ UP).dot(UP)-1) < .001, 'Unexpected Mixamo up orientation'
        references = {}
        for target, original in MAPPING.items():
            reference = destination_rest[target].to_quaternion()
            if target.startswith(('UpperArm.', 'Forearm.', 'Hand.')):
                direction = align @ (source_rest[original].to_quaternion() @ Y_AXIS)
                reference = (reference @ Y_AXIS).rotation_difference(direction) @ reference
            references[target] = reference
        source_count = max(2, math.ceil((last-first)/source_fps*RATE))
        samples = []
        for index in range(source_count+1):
            _frame(first+(last-first)*index/source_count)
            world = source.matrix_world
            points = {name: world @ source.pose.bones[actual].matrix.translation for name, actual in names.items()}
            rotations = {target: (align @ (world @ source.pose.bones[names[original]].matrix).to_quaternion()
                          @ source_rest[original].to_quaternion().inverted() @ align.inverted() @ references[target])
                         for target, original in MAPPING.items()}
            samples.append({'points': points, 'rotations': rotations})
        pole_stabilization = (_stable_source_poles(samples, destination_rest, align, leg_source)
                              if clip_name in ('Walk', 'Run') else None)
        start = samples[0]['points']['Hips'].copy()
        travel = samples[-1]['points']['Hips']-start
        travel.z = 0
        candidate_contact = ATTACK_CONTACT_PROGRESS if clip_name == 'Attack' else None
        count = max(2, round(duration*RATE))
        baked, target_positions = [], {'R': [], 'L': []}
        for index in range(count+1):
            progress = index/count
            source_progress = _time_attack(progress, candidate_contact, (last-first)/source_fps) if clip_name == 'Attack' else progress
            position = source_progress*source_count
            lower = min(source_count-1, math.floor(position))
            fraction = position-lower
            current, following = samples[lower], samples[lower+1]
            points = {name: current['points'][name].lerp(following['points'][name], fraction) for name in names}
            rotations = {name: current['rotations'][name].slerp(following['rotations'][name], fraction) for name in MAPPING}
            if clip_name == 'Attack':
                # The captured lunge moves a boot over a metre from the idle
                # stance. This stationary gameplay attack uses the same guard
                # lower-body layer as the descending and wide library cuts.
                _apply_pose(rig, idle_guard)
            else:
                _reset(rig)
                pelvis = rig.pose.bones['Pelvis']
                displacement = points['Hips']-source_rest['Hips'].translation
                displacement.x -= start.x
                displacement.y -= start.y
                displacement -= travel*source_progress
                pelvis.matrix = Matrix.Translation(destination_rest['Pelvis'].translation+align @ displacement*scale) @ rotations['Pelvis'].to_matrix().to_4x4()
                pelvis.scale = (1, 1, 1)
                bpy.context.view_layer.update()
            for target in ('Spine', 'Chest', 'Head', 'UpperArm.R', 'Forearm.R', 'Hand.R', 'UpperArm.L', 'Forearm.L', 'Hand.L'):
                _set_rotation(rig, target, rotations[target])
            # The rigid glove has no finger joints; keep the carried shield
            # and sword near the forearm line without changing pronation.
            for suffix in ('R', 'L'):
                hand = rig.pose.bones['Hand.'+suffix]
                forearm = rig.pose.bones['Forearm.'+suffix]
                hand_axis = (hand.tail-hand.head).normalized()
                forearm_axis = (forearm.tail-forearm.head).normalized()
                angle = hand_axis.angle(forearm_axis)
                limit = math.radians(30)
                if angle > limit:
                    desired = forearm_axis.slerp(hand_axis, limit/angle)
                    _set_rotation(rig, hand.name, hand_axis.rotation_difference(desired) @ hand.matrix.to_quaternion())
            for suffix, side in (('R', 'Right'), ('L', 'Left')):
                if clip_name == 'Attack':
                    target_positions[suffix].append(rig.pose.bones['Foot.'+suffix].head.copy())
                    continue
                foot_rotation = rotations['Foot.'+suffix]
                foot_deformation = foot_rotation @ destination_rest['Foot.'+suffix].to_quaternion().inverted()
                minimum = min((foot_deformation @ point).z for point in soles[suffix])
                source_clearance = max(0, min(points[side+'Foot'].z-source_rest[side+'Foot'].translation.z,
                                               points[side+'ToeBase'].z-source_rest[side+'ToeBase'].translation.z))
                ankle = align @ (points[side+'Foot']-Vector((start.x, start.y, 0))-travel*source_progress)*scale
                ankle.z = -minimum+.002+source_clearance*scale
                target_positions[suffix].append(ankle.copy())
                source_axis = (points[side+'Foot']-points[side+'UpLeg']).normalized()
                source_bend = points[side+'Leg']-points[side+'UpLeg']
                source_bend -= source_axis*source_bend.dot(source_axis)
                knee_direction = (current['knee_poles'][suffix].lerp(following['knee_poles'][suffix], fraction)
                                  if pole_stabilization else align @ source_bend)
                _solve_leg(rig, suffix, ankle, knee_direction, rotations)
            baked.append({bone.name: (bone.location.copy(), bone.rotation_euler.to_quaternion()) for bone in rig.pose.bones})
        native_targets = {suffix: [point.copy() for point in points] for suffix, points in target_positions.items()}
        # Distribute tiny source endpoint drift; do not introduce a forced snap.
        if clip_name != 'Attack':
            for index, pose in enumerate(baked):
                progress = index/count
                for name, (location, rotation) in list(pose.items()):
                    first_location, first_rotation = baked[0][name]
                    last_location, last_rotation = baked[-1][name]
                    correction = Quaternion().slerp(first_rotation @ last_rotation.inverted(), progress)
                    pose[name] = (location-(last_location-first_location)*progress, correction @ rotation)
        else:
            assert idle_guard is not None
            target_positions = _attack_guard_transitions(rig, baked, idle_guard, duration, soles)
        seam_smoothing = (_smooth_gait_seam(rig, baked, duration, soles, target_positions)
                          if clip_name in ('Walk', 'Run') else None)
        _new_action(rig, clip_name)
        previous = {}
        for index, pose in enumerate(baked):
            frame = 1+duration*FPS*index/count
            _frame(frame)
            _reset(rig)
            for name, (location, rotation) in pose.items():
                bone = rig.pose.bones[name]
                bone.location = location
                bone.rotation_euler = rotation.to_euler('XYZ', previous.get(name)) if name in previous else rotation.to_euler('XYZ')
                bone.scale = (1, 1, 1)
            _key(rig, frame, previous)
        if clip_name == 'Idle':
            idle_guard = {name: (location.copy(), rotation.copy()) for name, (location, rotation) in baked[0].items()}
        distance = -travel.y*scale if clip_name in ('Walk', 'Run') else 0
        contacts, contact_diagnostics = _final_contacts(rig, soles, duration, distance, target_positions, native_targets)
        info = {'source_file': filename, 'source_sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                'source_action': source_action.name, 'source_fps': source_fps,
                'source_frame_range': [first, last], 'source_duration_s': (last-first)/source_fps,
                'duration_s': duration, 'first_frame': 1, 'last_frame': 1+duration*FPS,
                'loop': clip_name != 'Attack', 'retarget_leg_scale': scale,
                'source_forward_travel_m': -travel.y, 'contact_intervals_s': contacts,
                'contact_method': 'final_keyed_actual_boot_clearance', 'contact_diagnostics': contact_diagnostics,
                'fixed_attack_feet': False}
        if clip_name in ('Walk', 'Run'):
            assert -travel.y > .2, ('Source must move forwards, not backpedal', filename, tuple(travel))
            distance = -travel.y*scale
            assert distance > .2, ('Missing real source forward travel', filename, distance)
            metadata[clip_name.lower()+'_cycle_m'] = distance
            info['cycle_distance_m'] = distance
            info['seam_smoothing'] = seam_smoothing
            info['knee_pole_stabilization'] = pole_stabilization
        if clip_name == 'Attack':
            contact_diagnostics['maximum_native_source_ankle_difference_m'] = None
            contact_diagnostics['native_source_leg_fidelity'] = 'not_applicable_preserved_idle_guard_lower_body'
            info.update(impact_time_s=ATTACK_IMPACT_TIME, source_contact_progress=candidate_contact,
                        animation_layer='captured_upper_body_on_idle_guard_base',
                        preserved_idle_bones=list(GUARD_BASE_BONES), captured_bones=list(UPPER_BODY_BONES),
                        fixed_attack_feet=True,
                        guard_entry_duration_s=ATTACK_ENTRY_DURATION, guard_recovery_duration_s=ATTACK_RECOVERY_DURATION,
                        guard_transition_method='fixed_world_quaternion_hermite_C1_with_sole_preserving_ik',
                        preparation_source_duration_s=ATTACK_PREPARATION_SOURCE_DURATION,
                        presentation_preparation_start_frame=first+(last-first)*max(0, candidate_contact-ATTACK_PREPARATION_SOURCE_DURATION/((last-first)/source_fps)),
                        source_contact_selection='reviewed Slash(3) fitted blade maximum forward reach; source frame about 25.9 of 1..48')
            info.update(_attack_presentation(first, last, source_fps, candidate_contact, ATTACK_RECOVERY_DURATION))
        metadata['clips'][clip_name] = info
        for obj in objects:
            bpy.data.objects.remove(obj, do_unlink=True)
        for action in source_actions:
            bpy.data.actions.remove(action)
        bpy.context.scene.render.fps = FPS
        bpy.context.scene.render.fps_base = 1
        print('[MIXAMO BAKED]', clip_name, filename, duration, metadata.get(clip_name.lower()+'_cycle_m'))
    metadata['clips'].update(_retarget_extra_attacks(rig, source_dir, destination_rest, soles, idle_guard))
    metadata['basic_attack_clips'] = ['Attack', 'Attack2', 'Attack3']
    metadata['heavy_attack_clip'] = 'HeavyAttack'
    _assert_preserved(rig, body, snapshot)
    rig.animation_data.action = bpy.data.actions['Idle']
    _frame(1)
    return metadata
