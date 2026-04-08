import axios from "axios";

export const getBranchesWithCommits = async (owner: string, repo: string) => {
  const res = await axios.get(
    `http://localhost:5000/api/repos/${owner}/${repo}/branches-with-commits`
  );
  return res.data;
};