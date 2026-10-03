import importlib.util
import json
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location("bootstrap",ROOT/"scripts/catalog-import-languages.py")
bootstrap=importlib.util.module_from_spec(spec)
spec.loader.exec_module(bootstrap)

class CatalogBootstrapTests(unittest.TestCase):
    def test_report_ignores_log_objects_and_reads_final_catalog_report(self):
        text='{"EventId":1,"Message":"untrusted data"}\n'+json.dumps({"sync":{"status":"completed"},"preparation":{"complete":True}})
        self.assertEqual("completed",bootstrap.parse_report(text)["sync"]["status"])
    def test_incomplete_assets_are_partial_even_when_metadata_completed(self):
        self.assertEqual("partial",bootstrap.classify(1,{"sync":{"status":"completed"},"preparation":{"complete":False}}))
    def test_oom_never_becomes_completed_from_a_report(self):
        self.assertEqual("failed",bootstrap.classify(137,{"sync":{"status":"completed"},"preparation":{"complete":True}}))
    def test_resume_keeps_existing_container_instead_of_launching_again(self):
        launches=[]
        self.assertEqual("existing",bootstrap.ensure_container("job",lambda _:True,lambda _:launches.append(True)))
        self.assertEqual([],launches)
    def test_all_endpoint_locales_are_present_and_unknown_cannot_launch(self):
        self.assertEqual(18,len(bootstrap.validate_languages(bootstrap.LANGUAGES)))
        with self.assertRaises(ValueError):bootstrap.validate_languages(["en;bad"])
if __name__=="__main__":unittest.main()
