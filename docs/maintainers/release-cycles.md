# Release Cycles

BTCPay Server uses three release types.

## Critical Releases

Critical releases address major bugs or security vulnerabilities that require immediate attention, including newly introduced workflow blockers without an easy workaround, migration failures, and defects that make a server unusable. They are expedited and may be published immediately.

- Nicolas Dorier oversees critical releases.
- Kukks is the secondary lead.
- Pavlenex publishes announcements across communication channels.

## Minor Releases

Minor releases collect small fixes and improvements merged since the previous release. The team reaches consensus before publishing them. They are planned every two to three weeks.

- Pavlenex structures the release and assigns issues to team members.
- Nicolas Dorier and Kukks publish the GitHub release.

## Major Releases

Major releases contain significant features and enhancements and are scheduled every two to three months. They receive broader community testing, a formal announcement, and a detailed blog post.

Feature freeze starts one week before a major release. During the freeze, maintainers stop adding features and focus on testing and bug fixes. Release candidates are then published for contributor and community testing. After reported release-candidate issues are resolved, the final release is tagged and published.

Use the [release checklist](release-checklist.md) when preparing any release.
