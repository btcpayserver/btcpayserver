# Reporting a potential Vulnerability.
<!-- Short intro. -->
We take the security of our project seriously, and we encourage responsible disclosure of any vulnerabilities that may be found. To facilitate this process, we have established the following vulnerability reporting process. 

We appreciate your efforts to disclose your findings responsibly.

##### 1. Reporting Channel
If you believe you have discovered a vulnerability in our project, please [report it privately through GitHub Security Advisories](https://github.com/btcpayserver/btcpayserver/security/advisories/new). You can also access this form by selecting **Report a vulnerability** on the repository's **Security** tab. Please do not report vulnerabilities in public issues, discussions, or pull requests.

Please allow for up to 2 business days for an acknowledgement of receipt. If you receive no response within 2 business days, please follow up with a comment on the private advisory.

Upon review of your report, you may be asked to provide additional information or guidance.

##### 2. In-Scope
<!-- What's in scope? Any repo in our org for example. -->
We welcome reports of vulnerabilities in repositories owned by the [BTCPay Server Github Organization](https://github.com/btcpayserver). This includes any issues related to the confidentiality, integrity, or availability of systems or data in these systems.

##### 3. Out of Scope
<!-- What's out of scope? Thinking here about custom deployments, plugins that are not created by BTCPay (this includes kukks plugins that should be reported to him directly). -->
1. Any BTCPay Server deployment that has been customized in any way. To facilitate reproducibility, please verify that the BTCPay Server instance is based on the un-altered source-code or [Docker deployment](https://docs.btcpayserver.org/Docker/).
2. Any BTCPay Server plugin that is not authored by `btcpayserver` as stated by the author tag in-app.

##### 4. Preferred Reporting Template
<!-- Template example to guide reporter into including specific info that we'd appreciate be included in the report. -->
We encourage the use of a reporting template that includes a detailed description of the vulnerability, any evidence or proof of concept, and steps to reproduce the vulnerability.

Please find an example of a reporting template [at the end of this document](#7-reporting-template-example).

##### 5. Timeline for Remediation
<!-- Tentative 90 business day timeline for resolution. This is a typical industry standard, but have included wording to include the fact that we're a team of volonteers, and that we cannot guarantee it. -->
While we will work to remediate the reported vulnerability within 90 business days from the acknowledgment of the report, being a team of volunteers, we cannot guarantee this timeline to be accurate at all time.

We will provide regular updates to the reporter until the vulnerability is resolved.

##### 6. Timeline to Public Disclosure
<!-- No tentaive timeline, given it can differ based on multiple criterias, but we have to take into account the fact that a public disclosure of a full year is too much. Security by obscurity is rarely beneficial, especially for the uninformed end-users. -->
We will work with the reporter to define a suitable timeline to public disclosure once the vulnerability is remediated.

<!-- 
##### 7. More information
For more information on our complete vulnerability response process, please see our [documentation]()
-->

##### 7. Reporting Template Example
<!-- Simple template for users to take as example for vulnerability reporting. Contains the basic minimum information that we need to assess promptly a report. -->

Feel free to use the below template in the description of your private GitHub security advisory.

```
* Vulnerability description: [Provide a clear and concise description of the vulnerability]
* Impact: [Describe the potential impact of the vulnerability, ie. any potential consequences for the project, its users, or any third parties]
* Affected version(s): [Specify which version(s) of the project are affected by the vulnerability]
* Steps to reproduce & Proof of Concept: [Provide a step-by-step guide to reproduce the vulnerability, including any screenshots and code snippets you feel would help]
* Severity: [Provide your assessment of the severity of the vulnerability, using a scale such as Warning/Low/Medium/High/Critical]
* Mitigation or Fix: [Provide your recommendation for a solution or mitigation strategy for the vulnerability]

```
