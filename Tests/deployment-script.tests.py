"""Model-free deployment contract and executable rollback projection tests."""
import os
import shutil
import tempfile
import json
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]

class DeploymentTests(unittest.TestCase):
    def test_normal_paths_have_no_model_operations(self):
        for name in ("compose.yaml", "deploy/compose.curiosity.yaml", "scripts/ci-validate.sh", "scripts/deploy-curiosity.sh"):
            source = (ROOT / name).read_text()
            for forbidden in ("DeepAnalysis__", "DEEP_ANALYSIS_IMAGE", "OLLAMA_IMAGE", "nvidia-smi", "--gpus", "ollama pull", "classifier-service/Dockerfile", "Dockerfile.hardware-benchmark", "legacy_model_root", "--remove-orphans", "--human-review-backup", "cheap-triage-human-review.db", "HumanReview__"):
                self.assertNotIn(forbidden, source, name)
    def test_preserved_application_gates(self):
        source = (ROOT / "scripts/deploy-curiosity.sh").read_text()
        for required in ("verify-repository-identity.sh", "org.opencontainers.image.revision", "security-scan.sh", "--regex-maintenance backup", "/healthz", "/version", "flock -n", "--no-deps", "rollback-jsm.json", "verify_deployment \"$previous_sha\""):
            self.assertIn(required, source)
        self.assertLess(source.index('security-scan.sh" image'), source.index("replacement_started=true"))
    def test_old_manifest_rolls_back_jsm_without_model_variables(self):
        service = dict(image="${JSM_IMAGE_REFERENCE}", environment={"DeepAnalysis__BaseUrl":"http://deep-analysis:8081/", "JOBSEARCHMANAGER_SMTP_HOST":"mailpit"}, networks={"default":{},"classifier":{}}, volumes=["/data:/app/data","/keys:/keys"], ports=["8080:8080"], user="1001:1001")
        old = dict(services={"jsm":service,"ollama":{"image":"${OLLAMA_IMAGE_REFERENCE:?required}"},"deep-analysis":{"image":"${DEEP_ANALYSIS_IMAGE_REFERENCE:?required}"}}, networks={"default":{},"classifier":{"internal":True}})
        result = subprocess.run([sys.executable,str(ROOT/'scripts/jsm-rollback-manifest.py')],input=json.dumps(old),text=True,capture_output=True,check=True)
        new=json.loads(result.stdout)
        self.assertEqual(list(new['services']), ['jsm'])
        self.assertNotIn('OLLAMA',result.stdout)
        self.assertNotIn('DEEP_ANALYSIS',result.stdout)
        self.assertNotIn('DeepAnalysis',result.stdout)
        self.assertEqual(new['services']['jsm']['environment']['JOBSEARCHMANAGER_SMTP_HOST'],'mailpit')
        for key in ('volumes','ports','user'): self.assertEqual(new['services']['jsm'][key],service[key])
        self.assertEqual(new['networks'],{'default':{}})
    def test_model_free_manifest_is_also_valid_rollback_input(self):
        current={'services':{'jsm':{'image':'jsm:new','read_only':True}}}
        result=subprocess.run([sys.executable,str(ROOT/'scripts/jsm-rollback-manifest.py')],input=json.dumps(current),text=True,capture_output=True,check=True)
        self.assertTrue(json.loads(result.stdout)['services']['jsm']['read_only'])

    def test_compose_uninterpolated_bind_mounts_remain_binds(self):
        mount={"type":"volume","source":"${JSM_LAB_ROOT:-/home/codex/jsm-lab}/data/app","target":"/app/data","volume":{}}
        result=subprocess.run([sys.executable,str(ROOT/'scripts/jsm-rollback-manifest.py')], input=json.dumps({"services":{"jsm":{"volumes":[mount]}}}),text=True,capture_output=True,check=True)
        actual=json.loads(result.stdout)['services']['jsm']['volumes'][0]
        self.assertEqual(actual['type'],'bind')
        self.assertEqual(actual['source'],mount['source'])
        self.assertEqual(actual['target'],mount['target'])
        self.assertNotIn('volume',actual)

    @unittest.skipUnless(os.name != "nt" and shutil.which("bash"), "Executable shell test runs on Linux")
    def test_actual_replacement_success_and_failure_rollback(self):
        source = (ROOT / "scripts/deploy-curiosity.sh").read_text()
        body = source[source.index("rollback() {"):source.index("printf '%s\\n' \"$target_sha\" > \"$deployed_sha_file.tmp\"")]
        for fail in (False, True):
            with self.subTest(fail=fail), tempfile.TemporaryDirectory() as directory:
                root=Path(directory)
                (root/"rollback-jsm.json").write_text('{"services":{"jsm":{"image":"old"}}}')
                active=root/"active.json"; active.write_text("candidate")
                setup = """set -Eeuo pipefail
state_root="$1"; active_manifest="$1/active.json"; lab_root="$1"
previous_reference=jsm:old; previous_sha=old; target_sha=new
replacement_started=false
# This exercises the real deployment ERR trap and replacement/rollback body.
docker() { printf 'docker %s image=%s\n' "$*" "$JSM_IMAGE_REFERENCE" >> "$state_root/calls"; }
verify_deployment() { printf 'verify %s\n' "$1" >> "$state_root/calls"; if [[ "$1" == new && "$FAIL" == true ]]; then return 7; fi; }
"""
                result=subprocess.run(["bash","-c",setup+body,"test",directory],env={**os.environ,"FAIL":str(fail).lower()},capture_output=True,text=True)
                self.assertEqual(result.returncode,7 if fail else 0,result.stderr)
                calls=(root/"calls").read_text()
                self.assertIn("--no-deps --pull never --force-recreate jsm",calls)
                self.assertIn("verify new",calls)
                self.assertEqual(calls.count("docker "),2 if fail else 1)
                self.assertEqual("verify old" in calls,fail)
                self.assertEqual(active.read_text(),(root/"rollback-jsm.json").read_text() if fail else "candidate")

if __name__ == '__main__': unittest.main()
