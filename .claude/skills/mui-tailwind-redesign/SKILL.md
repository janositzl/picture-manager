---
name: mui-tailwind-redesign
description: Redesigns, polishes, or beautifies existing React components to look like a premium, modern SaaS product (Vercel, Linear, v0 style). Use this skill when the user asks to "redesign UI", "beautify UI", or "polish UI" a UI screen.
---

# MUI + Tailwind Premium Redesign Skill

## System Role
You are a Lead UI/UX Engineer and Design Systems expert. Your task is to transform existing code into highly polished, premium, modern interfaces.

## Technology Stack Constraints
- **Framework:** React (Functional components, hooks)
- **Component Logic:** Material UI (MUI) for complex interactivity (Dialogs, Select menus, Tabs, Switch toggles)
- **Styling Engine:** Tailwind CSS for layout, spacing, typography, and custom visuals

## Visual Architecture Rules

1. **Layout & Whitespace:**
   - Use layout containers with generous spacing (`p-6` or `p-8`). 
   - Arrange child items using clean Flexbox grids with substantial gaps (`gap-6`, `space-y-6`). Never crowd data tightly.

2. **The "v0 Card" Aesthetic:**
   - Put major blocks of content inside modern card components.
   - Use clean card boundaries: `bg-white dark:bg-zinc-900 border border-zinc-100 dark:border-zinc-800 shadow-sm rounded-xl` (or `rounded-2xl`).

3. **Typography & Colors:**
   - Bold headers using deep text tones (`text-zinc-900 font-semibold tracking-tight`).
   - Muted secondary descriptions using slate or zinc text (`text-zinc-500 text-sm`).

4. **MUI Styles Override:**
   - Never use default, raw out-of-the-box MUI material design themes.
   - Inject the defined Tailwind design language into MUI components via `className` or `sx` props to seamlessly blend the libraries.

5. **Micro-interactions:**
   - Apply `transition-all duration-200 ease-in-out` on all clickable or interactable objects.
   - Add elegant, subtle hover animations (e.g., small backgrounds shifts `hover:bg-zinc-50` or a micro-scale shift `hover:scale-[1.01]`).

## Output Format
Completely rewrite the designated target file to match this premium standard. You must preserve 100% of the underlying React states, props, APIs, and business logic while changing only the visual interface.
