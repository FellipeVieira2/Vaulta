# Native Vaulta login

## Design source

The editable [Login frame in Figma](https://www.figma.com/design/Ey1vEJNXx1oaSZbODMamWY?node-id=29-153) uses the existing Vaulta black/purple semantic variables, Inter typography, primary/secondary button instances and the imported SDS Input Field component. The Product Design and Figma plugins were used to inspect the existing system, create the native login design, obtain design context and inspect the rendered screenshot before implementation.

The screen contains email and password fields, password visibility, an email/password sign-in button, a recovery action, an existing-account-registration action and the collection tagline. Native layout scrolls on smaller screens and with enlarged text or the keyboard open. Interactive targets are at least 48 dp; text scaling, semantic labels, a heading and screen-reader error announcements are supported.

## Authentication behavior

`LoginPage` and `LoginViewModel` use the existing `IAuthenticationService.LoginAsync` and session/token handling. There are no social sign-in buttons or alternative authentication protocols. Successful sign-in opens the native marketplace home. Registration opens the existing signup screen. Legacy login/welcome navigation routes to this native page.

`LoginFlow` validates email and required password before transport, trims email without modifying the password, prevents duplicate requests, publishes loading/error state and bounds requests to 30 seconds. Leaving the screen cancels the request, suppresses late navigation and clears the password. Error messages use fixed, safe text for incorrect credentials, rate limiting, connectivity and timeout; raw exceptions or credentials are never rendered or logged by this flow.

The existing API has no password recovery endpoint. The recovery action currently displays “A recuperação de senha ainda não está disponível no aplicativo.” It does not claim to send a recovery email. Completing recovery requires a real backend identity recovery flow.

## Packaged typography

Inter Regular, Medium, SemiBold and Bold are the static TTF assets from the [official Inter 4.1 release](https://github.com/rsms/inter/releases/tag/v4.1), downloaded from the repository release archive. The original SIL Open Font License is saved as `src/Vaulta.App/Resources/Fonts/Inter-OFL.txt` and included in the application assets as `licenses/Inter-OFL.txt`. The four font files are explicitly included through `MauiFont`; native theme aliases are InterRegular, InterMedium, InterSemiBold and InterBold.

## Verification

Eleven focused unit cases were observed failing before implementation and then passing. They cover invalid fields without transport, exact credential forwarding, loading transitions, safe server errors and successful retry, duplicate-submission suppression, cancellation/late success, bounded timeout and offline handling. The full App.Core suite passed 134/134 cases after integration with the current shared changes. Android compilation and actual-device visual verification are coordinated by the parent integration task; the Figma screenshot is a design reference and does not establish a rendered-device match.
