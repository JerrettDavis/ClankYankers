Feature: Workspace responsiveness
  The workspace should stay visible, accessible, and usable across the core viewport matrix.

  Scenario Outline: Focused workspace chrome stays usable across the core viewport matrix
    Given I open the workspace at <width>x<height>
    Then the browser viewport stays locked without page overflow
    And the focused workspace actions stay visible and interactable
    When I open the studio navigation overlay
    Then the studio navigation remains fully visible

    Examples:
      | width | height |
      | 360   | 800    |
      | 390   | 844    |
      | 768   | 1024   |
      | 1280  | 800    |
      | 1440  | 900    |
      | 1920  | 1080   |

  Scenario Outline: Launch blade fields stay reachable across the core viewport matrix
    Given I open the workspace at <width>x<height>
    When I open the new session blade
    And I switch the launch connector to Claude
    Then the launch blade fields stay reachable and visible

    Examples:
      | width | height |
      | 360   | 800    |
      | 390   | 844    |
      | 768   | 1024   |
      | 1280  | 800    |
      | 1440  | 900    |
      | 1920  | 1080   |
