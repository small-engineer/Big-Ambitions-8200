PYTHON ?= python3
MANAGED_DIR ?= /Users/home/Library/Application Support/Steam/steamapps/common/Big Ambitions/Big Ambitions.app/Contents/Resources/Data/Managed

.PHONY: check compile-check build package

check:
	$(PYTHON) -B scripts/verify.py

compile-check: check
	$(PYTHON) -B scripts/compile_check.py --managed-dir "$(MANAGED_DIR)"

build: check
	$(PYTHON) -B scripts/compile_check.py --managed-dir "$(MANAGED_DIR)" --output-dir Output/Unit-8200

package: build
	$(PYTHON) -B scripts/release_archive.py pack Output/Unit-8200 dist/Unit-8200.zip
