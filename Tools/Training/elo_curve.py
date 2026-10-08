"""Bounded whole-run chart samples, retaining each time bucket's extrema."""
import math

MAXIMUM_POINTS = 200
MAXIMUM_BUCKETS = 96


def restore_curve(data):
    points = data['points']
    previous = -1
    for point in points:
        if (not isinstance(point['step'], int) or point['step'] <= previous or
                point['step'] > data['wins'] + data['losses'] or not math.isfinite(point['elo'])):
            raise ValueError('Invalid Elo chart samples.')
        previous = point['step']
    if points[0]['step'] != 0:
        raise ValueError('Missing starting Elo reference.')
    if data.get('curveVersion', 0) == 0:
        # The earlier writer kept only the baseline and latest 199 matches.
        # Mark that lost region; those ratings cannot be reconstructed honestly.
        data.update(curveVersion=1, curveStart=points[1]['step'] if len(points) > 1 and points[1]['step'] > 1 else 0,
                    curveBucketWidth=1)
    if data['curveVersion'] != 1:
        raise ValueError('Unsupported Elo curve format.')
    start, width = data['curveStart'], data['curveBucketWidth']
    if (not isinstance(start, int) or start < 0 or start > previous or
            not isinstance(width, int) or width < 1 or width & (width - 1) or
            (start > 0 and not any(point['step'] == start for point in points))):
        raise ValueError('Invalid Elo curve coverage.')
    compact_curve(data)


def append_point(data, step, elo):
    data['points'].append(dict(step=step, elo=elo))
    compact_curve(data)


def compact_curve(data):
    points = data['points']
    start, last, width = data['curveStart'], points[-1]['step'], data['curveBucketWidth']
    while last // width - start // width + 1 > MAXIMUM_BUCKETS:
        width *= 2
    data['curveBucketWidth'] = width
    selected = {points[0]['step']: points[0], last: points[-1]}
    buckets = {}
    for point in points[1:]:
        if point['step'] < start:
            raise ValueError('Chart sample lies inside an unavailable historical region.')
        if point['step'] == start:
            selected[start] = point
        key = point['step'] // width
        if key not in buckets:
            buckets[key] = [point, point]
        else:
            low, high = buckets[key]
            if (point['elo'], point['step']) < (low['elo'], low['step']):
                buckets[key][0] = point
            if (point['elo'], point['step']) > (high['elo'], high['step']):
                buckets[key][1] = point
    for low, high in buckets.values():
        selected[low['step']] = low
        selected[high['step']] = high
    data['points'] = [selected[step] for step in sorted(selected)]
    if len(data['points']) > MAXIMUM_POINTS:
        raise ValueError('Elo chart sample budget exceeded.')
