# Release Cycle Documentation

## Introduction: 
This document outlines the release cycle process for BTCPay Server, detailing the types of releases and the procedures associated with each.

## Release Types: 
BTCPay Server categorizes its releases into three main types:

### Critical Release:
* Purpose: Address major bugs or security vulnerabilities that require immediate attention. This includes recently introduced bugs that are blocking the workflow of some users without an easy workaround, migration bugs, and bugs that brick the server.
* Process: These releases are expedited and can be pushed immediately due to their urgency.
* Assignees: 
  * Nicolas Dorier is responsible for overseeing critical releases.
  * Kukks is the secondary lead on critical releases.
  * Pavlenex is responsible for pushing announcements across communication channels.

### Minor Release:
* Purpose: Includes a collection of small bug fixes and minor improvements.
* Process: Merged pull requests are accumulated over the period and released collectively. Team consensus is needed before a release is pushed.
* Frequency: Planned every two to three weeks.
* Assignees: 
  * Pavlenex is responsible for structuring releases and assigning the issues to team-members 
  * Nicolas Dorier and Kukks are responsible for publishing a release on GitHub 

### Major Release:
* Purpose: Incorporates significant feature updates and major enhancements.
* Frequency: Scheduled once every 2-3 months.
* Process: Accompanied by a formal announcement, a detailed blog post, and extensive testing.
* Community Involvement: Major releases often involve more extensive community testing and feedback.

#### Feature Freeze and Testing:
* Feature Freeze: One week before a major release, a feature freezes starts. During this period, no new features are added; the focus is on testing and bug fixing.
* Release Candidates: After the feature freeze, Release Candidates (RCs) are created for testing. These RCs are critical for identifying any last-minute issues that need resolution before the final release. After community and contributor testing of RCs, the major release is tagged and published.