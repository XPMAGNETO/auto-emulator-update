# Migration from v9

v10 does not execute or embed the v9 PowerShell updater.

Recommended migration path:
1. Keep v9 backups untouched.
2. Run v10's system/frontend scan.
3. Verify every detected installation path.
4. Mark frontend-owned installations appropriately.
5. Only enable automatic scheduled updates after successful manual checks.
6. Import old settings/history later through a dedicated migration tool; v10 does not silently reinterpret v9 state.
