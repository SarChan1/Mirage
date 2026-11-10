import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location('save_catalog', pathlib.Path(__file__).parents[1] / 'update-save-catalog.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class SaveCatalogTests(unittest.TestCase):
    def test_only_windows_save_data(self):
        output = module.convert({'Example': {'files': {
            '<home>/save': {'tags': ['save'], 'when': [{'os': 'windows'}]},
            '<home>/config': {'tags': ['config']},
            '<home>/linux': {'tags': ['save'], 'when': [{'os': 'linux'}]},
            '<base>/portable': {'tags': ['save']},
        }}})
        self.assertEqual(['<home>/save', '<base>/portable'], output[0]['paths'])

    def test_store_identifiers_and_aliases(self):
        output = module.convert({'Game': {'steam': {'id': 123}, 'id': {'steamExtra': [456]}, 'installDir': {'Game Folder': {}}, 'files': {'<base>/saves': {'tags': ['save']}}}, 'Other Name': {'alias': 'Game'}})
        self.assertEqual(['123', '456'], output[0]['steamIds'])
        self.assertEqual(['Other Name'], output[0]['aliases'])
        self.assertEqual(['Game Folder'], output[0]['installDirs'])

    def test_config_only_game_is_excluded(self):
        self.assertEqual([], module.convert({'Game': {'files': {'config.ini': {'tags': ['config']}}}}))

    def test_registry_save_rules_are_retained(self):
        output = module.convert({'Game': {'registry': {'HKEY_CURRENT_USER/Game': {'tags': ['save']}}}})
        self.assertEqual(['HKEY_CURRENT_USER/Game'], output[0]['registry'])

if __name__ == '__main__':
    unittest.main()
