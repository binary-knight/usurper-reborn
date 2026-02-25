import re, os, glob

def replace_rand_range(content):
    result = []
    i = 0
    pattern = 'GD.RandRange('
    while i < len(content):
        idx = content.find(pattern, i)
        if idx == -1:
            result.append(content[i:])
            break
        result.append(content[i:idx])
        # Find the matching close paren
        start = idx + len(pattern)
        depth = 1
        j = start
        while j < len(content) and depth > 0:
            if content[j] == '(':
                depth += 1
            elif content[j] == ')':
                depth -= 1
            j += 1
        # content[start:j-1] is "arg1, arg2"
        args_str = content[start:j-1]
        # Split on first top-level comma
        comma_idx = None
        depth2 = 0
        for k, ch in enumerate(args_str):
            if ch == '(':
                depth2 += 1
            elif ch == ')':
                depth2 -= 1
            elif ch == ',' and depth2 == 0:
                comma_idx = k
                break
        if comma_idx is None:
            # Can't parse - leave as-is
            result.append(pattern + args_str + ')')
            i = j
            continue
        arg1 = args_str[:comma_idx].strip()
        arg2 = args_str[comma_idx+1:].strip()
        # Simplify arg2
        minus_one = re.match(r'^(.*?)\s*-\s*1$', arg2)
        if minus_one:
            # e.g. "actions.Count - 1" -> "actions.Count"
            new_arg2 = minus_one.group(1).strip()
        elif re.match(r'^\d+$', arg2):
            # Pure integer - add 1
            new_arg2 = str(int(arg2) + 1)
        else:
            # Complex expression - wrap with + 1
            new_arg2 = f'({arg2}) + 1'
        result.append(f'Random.Shared.Next({arg1}, {new_arg2})')
        i = j
    return ''.join(result)

files = glob.glob('Scripts/**/*.cs', recursive=True)
count = 0
for f in files:
    with open(f, 'r', encoding='utf-8') as fh:
        content = fh.read()
    if 'GD.RandRange(' in content:
        new_content = replace_rand_range(content)
        with open(f, 'w', encoding='utf-8') as fh:
            fh.write(new_content)
        print(f'Updated: {f}')
        count += 1

print(f'\nTotal files updated: {count}')
