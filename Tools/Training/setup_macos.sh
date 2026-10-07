#!/bin/zsh
set -euo pipefail

# Reproducible scoped install. Does not change the user's system Python.
training_source_dir=${0:A:h}
training_env_dir=${BLOCKNATIONS_ML_ENV:-$HOME/.local/share/blocknations-ml/venv}
training_python=${BLOCKNATIONS_ML_PYTHON:-/usr/local/bin/python3.10}
command -v uv >/dev/null
"$training_python" -c 'import sys; assert (3,10,1) <= sys.version_info[:3] <= (3,10,12), "ML-Agents 1.1.0 needs Python 3.10.1–3.10.12"'
if [[ ! -x "$training_env_dir/bin/python" ]]; then
    uv venv --python "$training_python" "$training_env_dir"
fi
uv pip install --python "$training_env_dir/bin/python" --override "$training_source_dir/dependency-overrides.txt" -r "$training_source_dir/requirements.txt"
"$training_env_dir/bin/mlagents-learn" --help >/dev/null
print "Trainer environment ready: $training_env_dir"
