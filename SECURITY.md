# Security Policy

Please do not open public issues for vulnerabilities that could permit arbitrary code execution, malicious update substitution, path traversal, or checksum bypass.

For a real hosted repository, configure GitHub private vulnerability reporting in **Settings → Security → Private vulnerability reporting**.

Update safety rules:
- HTTPS sources only.
- Verify publisher SHA-256 when available.
- Stage archives before deployment.
- Prevent archive path traversal.
- Back up before replacement.
- Never execute downloaded binaries merely to determine an update version.
