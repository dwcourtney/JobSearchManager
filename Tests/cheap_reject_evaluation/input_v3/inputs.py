"""Frozen generic span selection. No labels, gold evidence, or employer inputs."""
from bisect import bisect_left

DUTY_HEADINGS = ('responsibilities', 'what you will do', "what you'll do",
                 'essential functions', 'job duties', 'your role', 'duties')
QUALIFICATION_HEADINGS = ('basic qualifications', 'minimum qualifications',
                          'required qualifications', 'qualifications',
                          'requirements', 'what you bring', 'required skills')


def merge_ranges(ranges):
    merged = []
    for start, end in sorted(ranges):
        if start >= end:
            continue
        if merged and start <= merged[-1][1]:
            merged[-1] = (merged[-1][0], max(end, merged[-1][1]))
        else:
            merged.append((start, end))
    return merged


def first_heading(body, headings):
    # Exact case-insensitive headings with word boundaries; no occupation terms.
    folded = body.lower()
    matches = []
    for heading in headings:
        pos = folded.find(heading)
        while pos >= 0:
            end = pos + len(heading)
            if (pos == 0 or not folded[pos-1].isalnum()) and (end == len(body) or not folded[end].isalnum()):
                matches.append(pos)
                break
            pos = folded.find(heading, pos+1)
    return min(matches) if matches else None


def select_ranges(body, offsets, capacity, strategy, gap_size):
    """Token-index ranges; bounded, chronological and nonduplicated."""
    n = len(offsets)
    if n <= capacity:
        return [(0, n)] if n else []
    if strategy == 'prefix':
        return [(0, capacity)] if capacity else []
    count = 2 if strategy == 'headtail' else 3
    usable = max(0, capacity - (count-1)*gap_size)
    if usable < count:
        return [(0, capacity)] if capacity else []
    if strategy == 'headtail':
        a = (usable+1)//2
        ranges = [(0, a), (n-(usable-a), n)]
    elif strategy == 'headmidtail':
        a = (usable+2)//3
        b = (usable-a+1)//2
        c = usable-a-b
        middle = (n-b)//2
        ranges = [(0, a), (middle, middle+b), (n-c, n)]
    elif strategy == 'sections':
        # 25% head, 50% first duty section, 25% first qualification section.
        # Missing headings fall back to middle and tail; never use gold evidence.
        a = usable//4
        b = usable//2
        c = usable-a-b
        starts = [x[0] for x in offsets]
        duty = first_heading(body, DUTY_HEADINGS)
        qualification = first_heading(body, QUALIFICATION_HEADINGS)
        middle = bisect_left(starts, duty) if duty is not None else (n-b)//2
        tail = bisect_left(starts, qualification) if qualification is not None else n-c
        middle = min(middle, n-b)
        tail = min(tail, n-c)
        ranges = [(0, a), (middle, middle+b), (tail, tail+c)]
    else:
        raise ValueError(strategy)
    # Spend overlap savings on additional head context, never duplicate text.
    ranges = merge_ranges(ranges)
    used = sum(e-s for s, e in ranges)
    head_end = ranges[0][1]
    while used < usable:
        head_end += usable-used
        ranges = merge_ranges(ranges+[(0, min(head_end, n))])
        used = sum(e-s for s, e in ranges)
    return ranges


def encode_text(title, body, tokenizer, representation):
    strategy = representation[:-3]
    limit = int(representation[-3:])
    title_ids = tokenizer.encode('Title: '+title, add_special_tokens=False)
    budget = limit-tokenizer.num_special_tokens_to_add(pair=False)
    if len(title_ids) > budget:
        return None, dict(titleOverflow=True)
    prefix = tokenizer.encode('\nBody: ', add_special_tokens=False)
    encoded = tokenizer(body, add_special_tokens=False, return_offsets_mapping=True)
    body_ids = encoded['input_ids']
    offsets = encoded['offset_mapping']
    gap = tokenizer.encode('\n[...]\n', add_special_tokens=False)
    ids = list(title_ids)
    ranges = []
    if len(title_ids)+len(prefix) <= budget:
        ranges = select_ranges(body, offsets, budget-len(title_ids)-len(prefix), strategy, len(gap))
        ids += prefix
        for j, (start, end) in enumerate(ranges):
            if j:
                ids += gap
            ids += body_ids[start:end]
    source_ranges = [(offsets[s][0], offsets[e-1][1]) for s, e in ranges]
    ids = tokenizer.build_inputs_with_special_tokens(ids)
    assert len(ids) <= limit and ids[1:len(title_ids)+1] == title_ids
    return dict(input_ids=ids, attention_mask=[1]*len(ids)), dict(
        titleOverflow=False, tokens=len(ids), bodyTokens=len(body_ids),
        truncated=sum(e-s for s, e in ranges) < len(body_ids),
        tokenRanges=ranges, sourceRanges=source_ranges)


def encode(row, tokenizer, view, limit=None, tail=False):
    # Compatibility with the frozen v2 training harness; limit encoded in view.
    assert not tail
    return encode_text(row['title'], row['body'], tokenizer, view)
