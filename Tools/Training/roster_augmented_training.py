"""Explicit development entry point for public-roster permutation training.

The normal trainer, existing runs and shipped inference are unchanged. Use only
an owned experimental run with roster-augmentation.json and feed-forward v2.
"""
import os

from roster_augmentation import RosterAugmentedEnvironment, load_recipe


def main():
    import dotnet_environment
    from rated_training import main as train

    if not os.environ.get('BLOCKNATIONS_SIMULATION_WORKER'):
        raise ValueError('Roster augmentation requires the standalone C# training environment.')
    recipe = load_recipe(os.environ['BLOCKNATIONS_RATING_RUN'])
    original = dotnet_environment.DotNetEnvironment
    def create(*args, **kwargs):
        return RosterAugmentedEnvironment(original(*args, **kwargs), recipe['seed'])
    # The project's existing environment factory resolves this constructor. The
    # composition keeps the authoritative environment's internal references raw.
    # This replacement is local to this explicitly launched trainer process.
    dotnet_environment.DotNetEnvironment = create
    try:
        train()
    finally:
        dotnet_environment.DotNetEnvironment = original


if __name__ == '__main__':
    main()
