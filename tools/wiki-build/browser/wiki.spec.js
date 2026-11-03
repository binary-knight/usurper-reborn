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
  // One axe pass per test: the items/ page is large and one test holding
  // three passes took 19 s of the 30 s default under no load.
  const open = async (page) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.route("https://fonts.googleapis.com/**", (route) =>
      route.abort(),
    );
    await page.goto("/wiki/en/" + route);
    await expect(page.locator("h1")).toHaveCount(1);
  };
  const fits = async (page) =>
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBe(true);
  const violations = async (page, tags) =>
    (await new AxeBuilder({ page }).withTags(tags).analyze()).violations.map(
      (v) => ({ id: v.id, nodes: v.nodes.map((n) => n.target) }),
    );
  const wcag21 = ["wcag2a", "wcag2aa", "wcag21aa"];
  test(`mobile and accessibility, first theme: ${route || "home"}`, async ({
    page,
  }) => {
    await open(page);
    await fits(page);
    expect(await violations(page, wcag21)).toEqual([]);
  });
  test(`mobile and accessibility, light theme: ${route || "home"}`, async ({
    page,
  }) => {
    await open(page);
    await page.getByRole("button", { name: "Light theme" }).click();
    await fits(page);
    expect(await violations(page, wcag21)).toEqual([]);
  });
  test(`mobile and accessibility, disclosures open: ${route || "home"}`, async ({
    page,
  }) => {
    await open(page);
    await page.getByRole("button", { name: "Light theme" }).click();
    for (const summary of await page.locator("summary").all())
      await summary.click();
    await fits(page);
    expect(await violations(page, ["wcag2a", "wcag2aa"])).toEqual([]);
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
