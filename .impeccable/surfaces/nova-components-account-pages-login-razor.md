---
version: 1
slug: "nova-components-account-pages-login-razor"
primary_target: "Nova/Components/Account/Pages/Login.razor"
related_targets: ["Nova/Components/Account/Pages/Register.razor","Nova/Components/Account/Pages/Manage/Index.razor","Nova/Components/Account/Shared/ManageLayout.razor","Nova/Components/Account/Shared/ManageNavMenu.razor","Nova/Components/Account/Shared/StatusMessage.razor","Nova/Components/Account/Shared/PasskeySubmit.razor","Nova/Components/Account/Shared/ExternalLoginPicker.razor","Nova/Components/Account/Shared/ShowRecoveryCodes.razor"]
---

# Account experience

**Mode:** Operate

## User success

A member can sign in, register, recover access, or manage their profile without re-reading the interface: the current account area is obvious from one glance, the task's form leads, and status or validation lands in a predictable bounded strip. Static SSR pages stay fast and keep working in every browser-tested flow.

## Fieldhouse Wayfinding

The account experience is a venue wall of quiet matte sign panels (The Hall of Panels). The directory wall names each real account area; the area that owns the current screen is the punched one. Matte enamel surfaces, punched markers, and printed label tape supply the grammar without simulating sport or turning state into decoration.

## Hierarchy

1. Site identity and the account route.
2. The directory wall of panels; the punched panel identifies the current area.
3. The working hall: area heading, status strip, then the task's form or content.
4. In-hall actions, ordered by function (primary action leads, quiet actions follow).
5. Footer.

## Responsive behavior

Desktop shows a bounded directory wall above the working hall. Mobile keeps the wall as a horizontally scrollable panel strip, ordered and labeled exactly as on desktop; the hall collapses to one column and actions become full width. Information order and actions remain unchanged across breakpoints.

## Interaction and accessibility

Preserve static SSR forms, antiforgery tokens, cookie-based status messages, and passkey custom-element behavior. Panels are real links with the existing navigation semantics; the active state is unambiguous and not color-only (edge rail plus text emphasis). Keep native controls, visible focus, minimum 2.75rem touch targets, semantic regions, and reduced-motion-safe behavior.

## Visual constraints

Use the kelp palette: Wayfinding Teal for the punched panel and primary actions, Copper Rust for errors and destructive actions, Kelp Olive for confirmed success, Signal Amber only for bounded attention notices. Paper White boards with a one-pixel quiet border, flat (no ambient shadow), control radius 0.25rem, board radius 0.375rem. No gradients, glass, scoreboard styling, thick decorative side borders, or equal-weight card grids that obscure working hierarchy.
