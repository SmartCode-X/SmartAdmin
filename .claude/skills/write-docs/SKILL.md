---
name: write-docs
description: 写或改 site/ 下的文档——中文母版、英文译文的口吻、标点、开头、破折号规矩 + 闸门。当要写或编辑 site/ 下任何一页、把文档翻成英文、润色文档文案时使用。
argument-hint: "[site 下的页面路径]"
---

读仓库根目录的 `skills/write-docs.md` 并严格按它执行。它是单一真源,本文件只是入口包装。机器可查的那一半由 `site/scripts/lint-prose.mjs` 把关(`cd site && npm run lint:prose -- <page>`)。
