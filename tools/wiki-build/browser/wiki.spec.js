const { test, expect } = require("@playwright/test");
const AxeBuilder = require("@axe-core/playwright").default;
for (const route of [
  "",
  "characters/mental/",
  "characters/classes/Warrior/",
  "characters/abilities/power_strike/",
  "characters/spells/Cleric-1/",
  "items/",
  "items/1000/",
  "gods/terran/",
  "monsters/families/undead/",
  "monsters/bosses/",
  "online/discord/",
  "reference/balance/",
]) {
  test(`mobile, themes and accessibility: ${route || "home"}`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.route("https://fonts.googleapis.com/**", (route) =>
      route.abort(),
    );
    await page.goto("/wiki/en/" + route);
    await expect(page.locator("h1")).toHaveCount(1);
    for (let theme = 0; theme < 2; theme++) {
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= innerWidth,
        ),
      ).toBe(true);
      const results = await new AxeBuilder({ page })
        .withTags(["wcag2a", "wcag2aa", "wcag21aa"])
        .analyze();
      expect(
        results.violations.map((v) => ({
          id: v.id,
          nodes: v.nodes.map((n) => n.target),
        })),
      ).toEqual([]);
      await page.getByRole("button", { name: "Light theme" }).click();
    }
    for (const summary of await page.locator("summary").all())
      await summary.click();
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
    const opened = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa"])
      .analyze();
    expect(
      opened.violations.map((v) => ({
        id: v.id,
        nodes: v.nodes.map((n) => n.target),
      })),
    ).toEqual([]);
  });
}
test("keyboard skip, disclosure, search and persistent theme", async ({
  page,
}) => {
  await page.goto("/wiki/en/characters/mental/");
  await page.keyboard.press("Tab");
  await expect(
    page.getByText("Skip to content", { exact: true }),
  ).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.locator("main")).toBeFocused();
  await page.locator("summary").focus();
  await page.keyboard.press("Enter");
  await expect(page.locator("details")).toHaveAttribute("open", "");
  await page.getByRole("searchbox").fill("Terran");
  await expect(page.locator("#search-results")).toContainText("Terran");
  await page.getByRole("button", { name: "Light theme" }).click();
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
  await page.getByRole("searchbox").fill("zzzznonexistent");
  await expect(page.getByRole("status")).toContainText("No matching pages");
});
