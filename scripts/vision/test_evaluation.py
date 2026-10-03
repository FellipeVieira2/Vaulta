import unittest
from evaluation import normalize_embedding, rank_candidates, extract_embedding

class EvaluationTests(unittest.TestCase):
    def test_nearest_reference_is_ranked_first(self):
        self.assertEqual(rank_candidates([3., 4.], [[-3., -4.], [3., 4.], [4., -3.]], 2), [1, 2])
    def test_invalid_embedding_does_not_become_a_reference(self):
        for invalid in ([0., 0.], [float('nan'), 1.], [float('inf'), 1.]):
            with self.assertRaises(ValueError): normalize_embedding(invalid)
    def test_dimension_mismatch_is_rejected(self):
        with self.assertRaises(ValueError): rank_candidates([1., 0.], [[1., 0., 0.]], 1)

    def test_transformer_output_uses_cls_not_mean_of_patch_tokens(self):
        import numpy as np
        np.testing.assert_allclose(extract_embedding(np.array([[[3.,4.],[4.,-3.]]]), 'cls'), [.6,.8], rtol=1e-6)

if __name__ == '__main__': unittest.main()