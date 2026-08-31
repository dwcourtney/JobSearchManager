import json
import threading
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer
from unittest.mock import patch

import server


class ContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.httpd = ThreadingHTTPServer(("127.0.0.1", 0), server.Handler)
        cls.thread = threading.Thread(target=cls.httpd.serve_forever, daemon=True)
        cls.thread.start()
        cls.base = f"http://127.0.0.1:{cls.httpd.server_port}"

    @classmethod
    def tearDownClass(cls):
        cls.httpd.shutdown()
        cls.thread.join()
        cls.httpd.server_close()

    def request(self, path, payload=None, raw=None):
        data = raw if raw is not None else (json.dumps(payload).encode() if payload is not None else None)
        request = urllib.request.Request(
            self.base + path, data=data,
            headers={"Content-Type": "application/json"} if data is not None else {})
        try:
            with urllib.request.urlopen(request) as response:
                return response.status, json.load(response)
        except urllib.error.HTTPError as error:
            return error.code, json.load(error)

    @patch("server.gpu_diagnostic", return_value={
        "gpuAvailable": False, "deviceCount": 0, "deviceName": None,
        "vramTotalMiB": None, "vramUsedMiB": None, "driverVersion": None})
    def test_health_and_classification_contract(self, _probe):
        status, health = self.request("/healthz")
        self.assertEqual(200, status)
        self.assertEqual("healthy", health["status"])
        self.assertEqual("0.1.0", health["serviceVersion"])

        description = "exactly 18 chars!!"
        status, result = self.request("/classify", {
            "jobId": "R180395", "title": "Senior Software Developer",
            "description": description})
        self.assertEqual(200, status)
        self.assertTrue(result["received"])
        self.assertEqual("R180395", result["jobId"])
        self.assertEqual("Senior Software Developer", result["title"])
        self.assertEqual(len(description), result["descriptionLength"])

    def test_malformed_and_missing_job_id_fail_safely(self):
        status, malformed = self.request("/classify", raw=b"{")
        self.assertEqual(400, status)
        self.assertEqual("malformed JSON", malformed["error"])
        status, missing = self.request("/classify", {"title": "T", "description": "D"})
        self.assertEqual(400, status)
        self.assertIn("jobId", missing["fields"])

    @patch("server.subprocess.run")
    def test_gpu_schema_is_deterministic(self, run):
        run.return_value.stdout = "NVIDIA GeForce GTX 1070, 8192, 21, 580.65\n"
        result = server.gpu_diagnostic()
        self.assertEqual({
            "gpuAvailable": True, "deviceCount": 1,
            "deviceName": "NVIDIA GeForce GTX 1070", "vramTotalMiB": 8192,
            "vramUsedMiB": 21, "driverVersion": "580.65"}, result)


if __name__ == "__main__":
    unittest.main()
