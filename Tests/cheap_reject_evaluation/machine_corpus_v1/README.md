# Machine-adjudicated corpus preparation v1

Status: **3,198 postings directly adjudicated by Codex** under the user's clarified authorization. No Qwen or other model service was used. See `adjudication-report.md`, `machine-labeled.jsonl`, and `adjudication-manifest.json` for results, provenance and evidence limits.

The frozen sample contains 3,198 real postings from 11 employers after production-holdout/prior-evaluation exclusions and identity/near-copy deduplication. It is a census of the available deduplicated frame because that frame is smaller than the 3,500 target. No class balancing or employer quotas were applied. The frame is not representative of the general labor market: Leidos accounts for 1,789/3,198 (55.94%), followed by Boeing 581 and NVIDIA 340.

Only 1,312 postings have cached bodies; 1,886 are title/listing-only records. Do not infer nonexistent duties. Missing-body records must remain explicitly distinguishable during eventual labeling and training selection. Two read-only attempts to hydrate sampled Leidos descriptions returned HTTP 404; that does not establish that every missing description is unavailable. No production cache was modified.

`sample-unlabeled.jsonl` is the frozen public-field sample. `sampling-manifest.json` contains version counts, employer coverage, duplicate clusters, lexical title-family clusters, selection probabilities, source hashes and deterministic selection rules. The two eligible source files allow exact sampling replay. Inventory and export audits preserve original source locations and hashes. Exclusion indices contain digests only; no holdout text or labels are exported.

Reproduce sampling using Python 3.12 and scikit-learn 1.5.2:

```sh
python sample_corpus.py --input linux-eligible.jsonl --input windows-eligible.jsonl --target 3500 --output /tmp/jsm-corpus-replayed.jsonl --manifest /tmp/jsm-corpus-replayed-manifest.json
python -m unittest discover -s . -p 'test_*.py' -v
```

Source inventory/export is read-only. `inventory_sources.py` reads only selected job-cache JSONs; `make_exclusions.py` derives title, content, body, URL, stable-ID and employer/requisition identity digests from explicit exclusion manifests; `export_sources.py` removes matching records before exporting only public posting fields. Neither labels, fit scores, user settings nor production credentials are exported.

Employer/requisition identity deduplication chooses an available body before recency, then the newest whole cached version. Near-copy deduplication requires same employer, title character-TFIDF cosine >=0.90, and five-word body-shingle Jaccard >=0.95. Lexical title-family connected components use cosine >=0.80. These are corpus grouping operations, not production triage rules or a trained classifier. Missing-body near-copy detection and semantic family coverage remain limited.

No production behavior, RegEx rules, classifier training/deployment, commits or application dependencies have changed. Labels are provisional machine judgments, not human gold. Every title was reviewed; descriptions were reviewed through recorded excerpts and targeted additional spans, not exhaustively in full. Missing descriptions receive at most medium confidence. The human-review queue contains 281 cases, including 201 low-confidence cases. Forty-five targeted second reads are preserved in the audit trail.

Reproduce adjudicated artifacts from the frozen explicit decisions (no model inference):

```sh
python -X utf8 quality_reviews.py
python -X utf8 build_labeled_corpus.py
python -B -X utf8 -m unittest discover -s . -p 'test_*.py' -v
```

The scripts serialize recorded Codex judgments; they do not generate new semantic labels. See `validation-results.md` for checks and the exact adjudication file list.
