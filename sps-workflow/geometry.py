"""Pure geometry for reviewed recipe inputs. Consistent caller-supplied length units.

These calculations do not infer a manufacturing specification or modify CAD.
"""
import math


def vector(v):
    if len(v) != 3 or any(not math.isfinite(x) for x in v):
        raise ValueError("Finite three-vector required")
    return tuple(v)


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def unit(v):
    v = vector(v); n = math.sqrt(dot(v, v))
    if n <= 0: raise ValueError("Nonzero direction required")
    return tuple(x/n for x in v)


def source_axis_radius_point(axis_point, direction, center, radius, positive=True):
    """Intersect an actual offset source axis with a sphere about a bend center.

    Valid for the planar radial elbow-end datum case. Do not replace the source
    axis with a ray through center; native torus/connection checks remain required.
    """
    p, c, u = vector(axis_point), vector(center), unit(direction)
    if not math.isfinite(radius) or radius <= 0: raise ValueError("Positive radius required")
    offset = tuple(p[i]-c[i] for i in range(3))
    t = dot(offset, u)
    q = tuple(p[i]-t*u[i] for i in range(3))
    d2 = sum((q[i]-c[i])**2 for i in range(3))
    if d2 > radius*radius: raise ValueError("Axis misses the requested radius")
    distance = math.sqrt(max(0, radius*radius-d2)) * (1 if positive else -1)
    return tuple(q[i]+distance*u[i] for i in range(3))


def two_arc_reducer_radius(y, large_radius, small_radius, height, normal_offset=0):
    """Two equal, tangent broad arcs; inward normal offset, not radial subtraction.

    y runs from the large end to the small end. Only use after confirming this
    profile family against native source geometry; other reducer types differ.
    """
    if any(not math.isfinite(x) for x in (y, large_radius, small_radius, height, normal_offset)):
        raise ValueError("Finite reducer values required")
    difference = large_radius-small_radius
    if not (large_radius > small_radius > 0 and height > difference and 0 <= y <= height):
        raise ValueError("Unsupported two-arc reducer dimensions or station")
    r = (height*height+difference*difference)/(4*difference)
    if not 0 <= normal_offset < min(small_radius, r):
        raise ValueError("Unsupported inward offset")
    transition = height*(r-normal_offset)/(2*r)
    if y <= transition:
        return large_radius-r+math.sqrt(max(0, (r-normal_offset)**2-y*y))
    return small_radius+r-math.sqrt(max(0, (r+normal_offset)**2-(y-height)**2))


def reducer_cylinder_guide(large_radius, small_radius, height, normal_offset,
                           parent_center_y, parent_radius, bracket, samples=256):
    """Closed mid-wall curve for perpendicular Y-axis reducer/X-axis cylinder.

    Explicit bracket must straddle a root at every angle. No guessed fallback.
    Points are in the reducer's local frame. Header placement is a separate
    reviewed rigid transform. Parent radius may be outer or mid-wall as reviewed.
    """
    if not isinstance(samples, int) or samples < 16: raise ValueError("At least 16 samples required")
    lo0, hi0 = bracket
    if not (math.isfinite(parent_center_y) and math.isfinite(parent_radius) and parent_radius > 0
            and 0 <= lo0 < hi0 <= height): raise ValueError("Valid parent and bracket required")
    points = []
    for k in range(samples):
        a = 2*math.pi*k/samples
        def radius(y):
            return two_arc_reducer_radius(y, large_radius, small_radius, height, normal_offset)
        def residual(y):
            return (y-parent_center_y)**2+(radius(y)*math.sin(a))**2-parent_radius**2
        lo, hi = lo0, hi0; flo, fhi = residual(lo), residual(hi)
        if flo*fhi > 0: raise ValueError("Bracket does not straddle the intersection")
        for _ in range(70):
            mid = (lo+hi)/2; fm = residual(mid)
            if flo*fm <= 0: hi = mid
            else: lo, flo = mid, fm
        y = (lo+hi)/2; r = radius(y)
        points.append((r*math.cos(a), y, r*math.sin(a)))
    return points+[points[0]]


def bolt_centers(pcd, count, clocking_radians):
    """Explicit bolt count prevents corner arcs being counted as holes."""
    if not isinstance(count, int) or count < 1 or not math.isfinite(pcd) or pcd <= 0 or not math.isfinite(clocking_radians):
        raise ValueError("Positive PCD, integer count and finite clocking required")
    return [(pcd/2*math.cos(clocking_radians+2*math.pi*k/count),
             pcd/2*math.sin(clocking_radians+2*math.pi*k/count)) for k in range(count)]
