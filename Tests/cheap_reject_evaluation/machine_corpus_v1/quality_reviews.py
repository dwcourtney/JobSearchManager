"""Replay explicit Codex second-read decisions; no automatic semantic labeling."""
import json
from adjudicate import ROOT, CODES, ordered, excerpts

# Ordinals address the frozen, sorted body partition. These choices were made
# by Codex after reading the specified additional source spans in this task.
REVIEWS = {
 120: ('E', 'Rocket-propulsion structural analysis and engineering design reviews.', 900, 2400),
 122: ('Xj', 'Contingent-labor fulfillment dominates, but system configuration/integration experience creates an unresolved boundary.', 900, 2400),
 259: ('Em', 'Industrial process optimization and production-system analysis are plausibly adjacent engineering.', 900, 3400),
 260: ('E', 'Systems integration, verification and translation into hardware/software requirements.', 900, 3400),
 308: ('Ixj', 'Integration error monitoring and root-cause resolution across VNDLY, Workday and ServiceNow; HR title understates technical duties.', 900, 2400),
 377: ('C', 'Digital forensics of phones/computers, operating systems and evidence-extraction tools.', 900, 2400),
 379: ('B', 'Law-enforcement training-program coordination and institutional research.', 900, 2400),
 455: ('F', 'Financial oversight, forecasting, budgeting and program-control reporting.', 900, 2400),
 460: ('T', 'Leads software engineers and software lifecycle delivery.', 900, 3400),
 461: ('B', 'Government subcontract administration and contractual/business leadership.', 900, 2400),
 463: ('T', 'Leads systems/software engineers delivering software lifecycle work.', 900, 3400),
 521: ('E', 'Model-based navigation software and navigation-performance analysis.', 900, 2400),
 570: ('E', 'Electronic-warfare system field integration and testing.', 900, 2400),
 587: ('E', 'Designs optical/mechanical test equipment and testing methods.', 900, 2400),
 632: ('B', 'Sources and procures goods/services and manages supplier contracts.', 900, 2400),
 636: ('S', 'Embedded software engineering and model-based/DevSecOps development.', 900, 2400),
 757: ('T', 'Hands-on GenAI reference architecture, production applications and integrations.', 900, 2400),
 927: ('F', 'Cost-proposal development, pricing strategy and pricing-team management.', 900, 2400),
 944: ('M', 'Proposal writing, compliance, reviews and submission management.', 900, 2400),
 1125: ('S', 'Software design, testing, integration and DevOps using C++, JavaScript and Python.', 900, 3400),
 1203: ('J', 'Product-line sustainment and logistics planning; engineering depth remains unclear.', 900, 3400),
 1236: ('X', 'Supplier schedule management mixed with resolving propulsion-component technical issues.', 900, 2400),
 1250: ('E', 'Enterprise mission-modeling and simulation technical authority.', 900, 2400),
 1252: ('E', 'Physics-based missile models and algorithms in MATLAB, Python and C/C++.', 900, 2400),
 32: ('O', 'Airfield construction, maintenance and operational coordination.', 1200, 2700),
 98: ('L', 'Language/cultural analysis and communication for intelligence audiences; no computational NLP duties established.', 1200, 2700),
 132: ('B', 'Operational case coordination, policy/legal liaison and intelligence briefings.', 1200, 2700),
 252: ('E', 'Troubleshoots electronics, controls, sensors, network cabling and fiber.', 1200, 2700),
 318: ('T', 'Radar architecture, embedded C, board bring-up, debugging and customer proofs of concept.', 1200, 2700),
 319: ('T', 'Robotics MCU selection, motor-control architecture and software-stack integration.', 1200, 2700),
 869: ('T', 'Architects automated Windows infrastructure using code in air-gapped environments.', 1200, 2700),
 1309: ('J', 'Lab operations and science liaison; technical-duty depth remains uncertain.', 1200, 2700),
 1310: ('O', 'Station operations, emergency management and personnel coordination.', 1200, 2700),
 1311: ('A', 'Reviewed text still does not establish primary duties beyond remote-site management.', 1200, 2700),
}
NEW_EXCERPT_REVIEWS = {
 104: ('E', 'Independent civil/structural engineering design reviews and calculations.'),
 116: ('X', 'Cost and schedule advisory work mixed with owners-engineering and technology reviews.'),
 226: ('T', 'Owns software architecture and requirements for rack-scale products.'),
 392: ('T', 'Hands-on AI datacenter deployment and hardware/software troubleshooting.'),
 573: ('S', 'Chromium/CEF code, upstream contributions and CI/CD infrastructure.'),
 590: ('S', 'Server-management firmware architecture and datacenter health workflows.'),
 613: ('S', 'Compute-platform architecture and automated infrastructure remediation.'),
 615: ('S', 'Rack-scale software architecture, Kubernetes controllers and open-source infrastructure.'),
 619: ('S', 'C++ mapping systems, 3D reconstruction and fleet-data pipelines.'),
 709: ('E', 'Relay settings, IEC61850 logic diagrams and protection engineering.'),
}

def generate():
    rows=ordered('body'); out=[]
    for i in sorted(set(REVIEWS)|set(NEW_EXCERPT_REVIEWS)|{1102}):
        r=rows[i]
        if i in REVIEWS:
            code,reason,start,end=REVIEWS[i]
            evidence=[dict(start=start,end=min(end,len(r['body'])),text=r['body'][start:end])]
        elif i in NEW_EXCERPT_REVIEWS:
            code,reason=NEW_EXCERPT_REVIEWS[i];evidence=excerpts(r)
        else:
            code='Sm';reason='Explicit ServiceNow software-engineering title, but the available description is boilerplate rather than duties.';evidence=[]
        label,category,_=CODES[code[0]]
        out.append(dict(id=r['id'],reviewOrdinal=i,label=label,category=category,
                        confidence='low' if label=='AMBIGUOUS' else ('medium' if 'm' in code else 'high'),
                        reason=reason,basis='title' if i==1102 else ('duties' if i==308 else 'both'),
                        reviewedEvidence=evidence,titleBodyConflict='x' in code,
                        technicalJargonDutiesConflict='j' in code,
                        descriptionInsufficient=i==1102,
                        reviewPurpose='Targeted Codex second read; not independent or random accuracy estimation'))
    (ROOT/'codex-quality-reviews.jsonl').write_text(''.join(json.dumps(r,ensure_ascii=False)+'\n' for r in out),encoding='utf-8',newline='\n')
    print('Explicit quality reviews:',len(out))

if __name__=='__main__':generate()
