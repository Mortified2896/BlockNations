"""Summarize actual arena selections separately from learner and board steps."""
import argparse
import json
from pathlib import Path


def summarize(record, gamma=.995, horizon=64):
    if record.get('version') != 1 or not 0 < gamma <= 1 or type(horizon) is not int or horizon < 1:
        raise ValueError('Use versioned decision diagnostics and explicit objective settings.')
    workers = record.get('workers', [])
    if not workers: raise ValueError('No measured decision-length histograms are available.')
    bounds = workers[0]['upperBounds']
    if any(w['upperBounds'] != bounds for w in workers): raise ValueError('Histogram contracts differ.')
    root = sum(w['rootSelections'] for w in workers)
    conditional = sum(w['conditionalSelections'] for w in workers)
    histograms = {key:[sum(w[key][i] for w in workers) for i in range(len(bounds))]
                  for key in ('completedSeatGameLengths','completedSeatTurnLengths')}
    return dict(version=1, source='all-seat-arena-decisions-not-PPO-learner-steps',
                selections=root+conditional, boardActions=record['boardActions'],
                sourceSelectionShare=root/(root+conditional) if root+conditional else None,
                selectionsPerBoardAction=(root+conditional)/record['boardActions'] if record['boardActions'] else None,
                conditionalEndTurns=sum(w['conditionalEndTurns'] for w in workers),
                upperBounds=bounds, **histograms, gamma=gamma, timeHorizon=horizon,
                discountArithmetic={str(n):gamma**n for n in (64, 100, 200, 500)},
                interpretation='Length histograms include both seats and assigned opponents; they do not prove a credit-assignment defect.')


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run',type=Path,required=True); parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    plan=json.loads((args.run/'trainer.yaml').read_text())
    options=next(iter(plan['behaviors'].values()))
    result=summarize(json.loads((args.run/'decision-diagnostics.json').read_text()),
                     options['reward_signals']['extrinsic']['gamma'],options['time_horizon'])
    with args.output.open('x') as output: json.dump(result,output,indent=2)
