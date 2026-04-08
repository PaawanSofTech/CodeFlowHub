import { api } from "./client";

export const getBranchesWithCommits = async (owner: string, repo: string) => {
  const res = await api.get(
    `/api/repos/${owner}/${repo}/branches-with-commits`
  );
  return res.data;
};