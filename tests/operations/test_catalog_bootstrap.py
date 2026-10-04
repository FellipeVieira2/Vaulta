import importlib.util
import json
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location("bootstrap",ROOT/"scripts/catalog-import-languages.py")
bootstrap=importlib.util.module_from_spec(spec)
spec.loader.exec_module(bootstrap)

class CatalogBootstrapTests(unittest.TestCase):
    def test_failed_and_partial_languages_have_bounded_new_attempts(self):
        run_id="c6228c1c-b6d7-479f-8d81-c3ac3913a434"
        self.assertEqual((2,"resume:"+run_id),bootstrap.retry_plan({"status":"partial","report":{"id":run_id}}))
        self.assertEqual((2,"all"),bootstrap.retry_plan({"status":"failed"}))
        self.assertIsNone(bootstrap.retry_plan({"status":"failed","attempts":3}))
        self.assertIsNone(bootstrap.retry_plan({"status":"completed"}))
        self.assertEqual((1,"all"),bootstrap.retry_plan({}))
    def test_retry_report_cannot_inject_container_arguments(self):
        self.assertEqual((2,"all"),bootstrap.retry_plan({"status":"partial","report":{"id":"bad; command"}}))
    def test_report_ignores_log_objects_and_reads_final_catalog_report(self):
        text='{"EventId":1,"Message":"untrusted data"}\n'+json.dumps({"sync":{"status":"completed"},"preparation":{"complete":True}})
        self.assertEqual("completed",bootstrap.parse_report(text)["sync"]["status"])
    def test_metadata_only_report_is_accepted(self):
        report=bootstrap.parse_report('{"id":"run","status":"completed","recordsRead":200}')
        self.assertEqual("completed",bootstrap.classify(0,report))
    def test_failed_artwork_prevents_complete_pipeline(self):
        state={"languages":{"en":{"status":"completed"}},"artwork":{"status":"failed"},"vision":{"status":"completed"}}
        self.assertEqual("completed_with_pending",bootstrap.pipeline_status(state,["en"]))
    def test_absent_requested_language_prevents_complete_pipeline(self):
        state={"languages":{"en":{"status":"completed"}},"artwork":{"status":"completed"},"vision":{"status":"completed"}}
        self.assertEqual("completed_with_pending",bootstrap.pipeline_status(state,["en","ja"]))
    def test_artwork_command_does_not_build_embeddings(self):
        self.assertIn("Vision__ModelManifestPath=",bootstrap.artwork_arguments())
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
