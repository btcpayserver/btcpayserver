---
name: btcpayserver-changelog
description: Use when updating or reviewing Changelog.md in BTCPayServer. Contains release range, inclusion, style, and verification guidance.
---

# BTCPayServer Changelog

Follow the [changelog conventions](../../../docs/maintainers/README.md#changelog). When asked to update or review the changelog, focus on user-visible changes and keep entries concise.

## Release Range

- Compare against the previous release tag, for example `v2.3.9..master` when preparing `2.4.0`.
- If the changelog branch contains changelog-only commits on top of `master`, compare against `master` to avoid including those commits in the review.
- Check whether the previous release tag is on the same ancestry path. If not, identify the practical post-release bump commit and compare from there as needed.

## Verification

- Review the final diff with `git diff -- Changelog.md`.
- Run `git diff --check -- Changelog.md` to catch whitespace issues.
- Verify authorship for added entries with `git show --no-patch --format='%h %an <%ae> %s' <commit>` when attribution is not obvious.
