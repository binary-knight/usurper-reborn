"""
Replace GD.Randf(), GD.Randi() with Random.Shared equivalents.
Delete entire lines containing GD.Print() and GD.PrintErr().
"""
import re, glob

def process_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    original = content

    # Replace GD.Randf() -> (float)Random.Shared.NextDouble()
    content = content.replace('GD.Randf()', '(float)Random.Shared.NextDouble()')

    # Replace GD.Randi() -> Random.Shared.Next()
    content = content.replace('GD.Randi()', 'Random.Shared.Next()')

    # Remove lines that contain GD.Print( or GD.PrintErr(
    # These are debug prints - remove the whole line
    lines = content.split('\n')
    new_lines = []
    for line in lines:
        # Skip lines that contain GD.Print( or GD.PrintErr(
        # but keep lines that only have those in comments
        stripped = line.strip()
        if re.search(r'\bGD\.Print(Err)?\s*\(', line):
            # Only remove if not purely a comment
            if not stripped.startswith('//') and not stripped.startswith('*'):
                continue  # Remove this line
        new_lines.append(line)
    content = '\n'.join(new_lines)

    if content != original:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        return True
    return False

files = glob.glob('Scripts/**/*.cs', recursive=True)
count = 0
for f in files:
    if process_file(f):
        print(f'Updated: {f}')
        count += 1

print(f'\nTotal files updated: {count}')

# Report any remaining GD. calls
remaining = []
for f in glob.glob('Scripts/**/*.cs', recursive=True):
    with open(f, 'r', encoding='utf-8') as fh:
        content = fh.read()
    matches = re.findall(r'GD\.\w+\(', content)
    if matches:
        remaining.append((f, matches))

if remaining:
    print('\nRemaining GD. calls:')
    for f, m in remaining:
        print(f'  {f}: {m}')
else:
    print('\nNo remaining GD. calls found.')
