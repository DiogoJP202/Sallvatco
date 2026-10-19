"""Corrige somente o terminador conhecido do SQL exportado; nunca edita migrations."""
from pathlib import Path
import sys

PREFIX = '''    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "MigrationId" = '20260929114848_AddAutomaticPaymentRecovery') THEN
    ALTER TABLE payment_recovery_execution ALTER COLUMN source DROP DEFAULT'''
OLD = PREFIX + "\n    END IF;"
FIXED = PREFIX + ";\n    END IF;"


def normalize(source):
    source = source.replace("\r\n", "\n")
    if source.count(OLD) == 1 and source.count(FIXED) == 0:
        return source.replace(OLD, FIXED)
    if source.count(FIXED) == 1 and source.count(OLD) == 0:
        return source
    raise ValueError("Formato do bloco SQL conhecido mudou; revisar geração antes de publicar.")


if __name__ == "__main__":
    path = Path(sys.argv[1])
    path.write_text(normalize(path.read_text(encoding="utf-8-sig")), encoding="utf-8", newline="\n")
    print("Terminador do bloco AddAutomaticPaymentRecovery validado; migrations originais preservadas.")
