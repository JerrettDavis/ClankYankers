Feature: Studio screenshots
  Capture reference screenshots of the studio UI across viewport sizes and colour schemes.
  These images are committed to docs/assets/bdd/ and refreshed by the weekly screenshots workflow.

  Scenario Outline: Overview page renders correctly at <label>
    Given I navigate to the overview page at <width>x<height> in <theme> mode
    Then a screenshot is saved as "ui-overview-<label>.png"

    Examples:
      | label            | width | height | theme |
      | desktop-light    | 1440  | 900    | light |
      | desktop-dark     | 1440  | 900    | dark  |
      | mobile-light     | 390   | 844    | light |
      | mobile-dark      | 390   | 844    | dark  |

  Scenario Outline: Sessions page renders correctly at <label>
    Given I navigate to the sessions page at <width>x<height> in <theme> mode
    Then a screenshot is saved as "ui-sessions-<label>.png"

    Examples:
      | label            | width | height | theme |
      | desktop-light    | 1440  | 900    | light |
      | desktop-dark     | 1440  | 900    | dark  |
      | mobile-light     | 390   | 844    | light |
