# LLM hardware benchmark

This workflow repeats the frozen production-holdout LLM experiment on temporary benchmark hardware. It is an evaluation workflow, not a production inference service.

## Scientific boundary

- `--llm-benchmark preflight` uses only a synthetic posting.
- `--llm-benchmark predict` validates the canonical holdout file and refuses to run if a reference, RegEx, GTX, score, or comparison artifact is present.
- The benchmark image is derived from the exact protected-main JSM image and removes packaged RegEx rule, regression-corpus, and sampling-plan files.
- The tinker compose project publishes no ports, uses a private classifier network, and has no production data, Mailpit, or restart policy.
- The first valid structured prediction for each posting is checkpointed. The complete 200-posting dataset is frozen before transfer.
- `--llm-benchmark score` runs only on curiosity after transfer. It validates canonical holdout, reference, GTX-prediction, taxonomy, model, prompt, and generation fingerprints before using the shared metric calculator.

The RTX result and cross-hardware comparison are imported into the configured production evaluation directory as immutable JSON evidence. The Admin UI reads those local artifacts; it never calls tinker.

## Required evidence

Archive the protected-main SHA and image IDs, input/output SHA-256 manifests, preflight report, durable prediction status/checkpoint/frozen dataset, one-second resource observation, RTX report, cross-hardware report, environment versions, CI/merge/deploy results, and production verification screenshots. Restore the Light theme after checking Light, Dark, Nord Polar Night, Nord Snow Storm, and Dracula.
